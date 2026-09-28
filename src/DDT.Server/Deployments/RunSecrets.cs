// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Core.Sequences;
using DDT.Core.Templates;
using DDT.Server.Accounts;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Sequences;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// The passwords a run needs, handed to its agent just in time: only for the step that needs them, only while that
// step runs, and every read audited. They are read from the settings, the stored accounts and the accounts given for
// the run at that moment, and only for the destination each was entered for. Nothing here logs them. The caller saves
// the audit rows before it answers.
public sealed class RunSecrets(
    DdtDbContext database,
    UnattendRenderer renderer,
    DdtSettings settings,
    AccountProtector accounts,
    RunCredentials credentials,
    TimeProvider timeProvider)
{
    public async Task<(string? AnswerFile, string? Refusal)> AnswerFileAsync(
        Machine machine,
        Guid runId,
        Guid stepId,
        string? address,
        CancellationToken cancellationToken)
    {
        (Deployment? run, SequenceStep? step, RunInputs? inputs, _, string? refusal) = await RunningStepAsync(machine, runId, stepId, cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null)
        {
            return (null, refusal);
        }

        if (step is not WriteUnattendStep unattend)
        {
            return (null, "That step writes no answer file.");
        }

        string? password = settings.Current.Deployment.LocalAdministrator.Password;

        if (unattend.LocalAdministrator && string.IsNullOrEmpty(password))
        {
            return (null, "The step adds the local administrator, but DDT:Deployment:LocalAdministrator has no password any more.");
        }

        string? language = await database.DeploymentArtifacts
            .AsNoTracking()
            .Where(a => a.DeploymentId == runId && a.Kind == ArtifactKind.Image)
            .Select(a => a.Language)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        (string? answerFile, string? problem) = renderer.Render(inputs!, unattend, language, password, TemplateValues(machine, run!));

        if (problem is not null)
        {
            return (null, problem);
        }

        Audit(run!, machine, address, $"The answer file of step {step.Name} ({stepId:D}) of {run!.Title}.");

        return (answerFile, null);
    }

    // The domain is the one configured when the run started: a domain named anywhere else could send the join
    // account to a foreign domain controller. A step that names an account joins that account's domain, or the one its
    // input declared, never one the sequence names. The join runs in Windows, so only the service there gets it.
    public async Task<(AgentJoinDomainCredentials? Credentials, string? Refusal)> JoinCredentialsAsync(
        Machine machine,
        Guid runId,
        Guid stepId,
        string? address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (machine.AgentEnvironment != AgentEnvironment.Windows)
        {
            return (null, "A machine joins its domain in Windows, and this agent registered from Windows PE.");
        }

        (Deployment? run, SequenceStep? step, RunInputs? inputs, SequenceDefinition? definition, string? refusal) = await RunningStepAsync(
                machine,
                runId,
                stepId,
                cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null)
        {
            return (null, refusal);
        }

        if (step is not JoinDomainStep join)
        {
            return (null, "That step joins no domain.");
        }

        if (join.Account is { } named)
        {
            return await AccountJoinAsync(machine, run!, definition!, join, named, inputs!, address, cancellationToken).ConfigureAwait(false);
        }

        DomainOptions domain = settings.Current.Deployment.Domain;

        if (string.IsNullOrWhiteSpace(domain.UserName) || string.IsNullOrEmpty(domain.Password))
        {
            return (null, "DDT:Deployment:Domain no longer names an account to join the domain with.");
        }

        if (inputs!.DomainName is not { } name || !string.Equals(name, domain.Name?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return (null, "The configured domain changed after the run started, so its account is not for this run. Assign the sequence again.");
        }

        (string? organizationalUnit, string? unitProblem) = OrganizationalUnit(join, inputs.DomainOrganizationalUnit, TemplateValues(machine, run!));

        if (unitProblem is not null)
        {
            return (null, unitProblem);
        }

        Audit(run!, machine, address, $"The domain join credentials of step {step.Name} ({stepId:D}) of {run!.Title}, for {name}.");

        return (new AgentJoinDomainCredentials(name, organizationalUnit, domain.UserName.Trim(), domain.Password), null);
    }

    // The step, as frozen with the run, when the machine's active run is running it, anywhere in the run's tree.
    private async Task<(Deployment? Run, SequenceStep? Step, RunInputs? Inputs, SequenceDefinition? Definition, string? Refusal)> RunningStepAsync(
        Machine machine,
        Guid runId,
        Guid stepId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        Deployment? run = machine.ActiveDeploymentId == runId
            ? await database.Deployments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == runId, cancellationToken).ConfigureAwait(false)
            : null;

        if (run is not { State: DeploymentState.Running, Inputs: { } inputs })
        {
            return (null, null, null, null, "This machine has no such run that is running. Report the run as running first.");
        }

        DeploymentStep? row = await database.DeploymentSteps
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.DeploymentId == runId && s.StepId == stepId, cancellationToken)
            .ConfigureAwait(false);

        if (row is not { State: StepState.Running })
        {
            return (null, null, null, null, "Only a step that is running gets what it needs. Report the step as running first.");
        }

        DeploymentSnapshot snapshot = await database.DeploymentSnapshots
            .AsNoTracking()
            .SingleAsync(s => s.DeploymentId == runId, cancellationToken)
            .ConfigureAwait(false);

        SequenceDefinition definition = SequenceDocuments.Read(snapshot.Definition);

        return (run, SequenceTree.Index(definition).GetValueOrDefault(stepId)?.Step, RunInputs.Read(inputs), definition, null);
    }

    // The accounts a step uses, while it runs: the account a script in Windows runs as, only for the service there and
    // only when the account lets scripts run as it, and the step's own shares to connect, each with its path worked out
    // here from the values and the machine's facts the run started with, never from what the agent reported since, and
    // only to a server its account names. Only a leaf step connects shares; a group, an IF or a Repeat gets nothing, whatever its document
    // holds. Every account is read from the run's own copy of the sequence. All or nothing: one refusal refuses the step.
    public async Task<(AgentStepAccounts? Accounts, string? Refusal)> StepAccountsAsync(
        Machine machine,
        Guid runId,
        Guid stepId,
        string? address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        (Deployment? run, SequenceStep? step, RunInputs? inputs, SequenceDefinition? definition, string? refusal) = await RunningStepAsync(
                machine,
                runId,
                stepId,
                cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null)
        {
            return (null, refusal);
        }

        if (step is { IsContainer: true })
        {
            return (null, "A group, an IF or a Repeat uses no account itself. Only the steps in it do.");
        }

        AccountReference? runAs = (step as RunScriptStep)?.RunAs;
        IReadOnlyList<ShareConnection?> shares = step?.Shares ?? [];

        if (step is null || (runAs is null && shares.Count == 0))
        {
            return (null, "That step uses no account.");
        }

        if (runAs is not null && step is not RunScriptStep { Phase: SequencePhase.Windows })
        {
            return (null, "Only a script in Windows runs as an account.");
        }

        if (runAs is not null && machine.AgentEnvironment != AgentEnvironment.Windows)
        {
            return (null, "A script runs as an account in Windows, and this agent registered from Windows PE.");
        }

        Dictionary<string, StepAccount> read = new(StringComparer.Ordinal);
        List<string> reads = [];
        AgentAccount? runAsAccount = null;

        async Task<(StepAccount? Account, string? Refusal)> ReadAsync(AccountReference reference)
        {
            string key = $"{reference.AccountId:D}|{reference.Input}";

            if (read.TryGetValue(key, out StepAccount? known))
            {
                return (known, null);
            }

            (StepAccount? account, string? refused) = await AccountAsync(run!, definition!, reference, cancellationToken).ConfigureAwait(false);

            if (account is not null)
            {
                read[key] = account;
            }

            return (account, refused);
        }

        if (runAs is not null)
        {
            (StepAccount? account, string? refused) = await ReadAsync(runAs).ConfigureAwait(false);

            if (refused is not null)
            {
                return (null, refused);
            }

            if (!account!.RunAs)
            {
                return (null, $"The {account.Describe} does not let scripts run as it.");
            }

            runAsAccount = new AgentAccount(account.UserName, account.Password);
            reads.Add($"The {account.Describe} of step {step.Name} ({stepId:D}) of {run!.Title}, to run the script as {account.UserName}.");
        }

        Func<string, string?> values = ShareValues(machine, run!, inputs!);
        List<AgentShareConnection> connections = [];

        for (int index = 0; index < shares.Count; index++)
        {
            ShareConnection? share = shares[index];
            string which = $"share {index + 1} of {step.Name}";

            if (share is not { Account: { } reference, Path: { } written })
            {
                return (null, $"The {which} names no path or no account.");
            }

            if (!ValueTemplate.TryRender(written, values, out string path, out TemplateProblem? problem))
            {
                return (null, $"The path {written} of {which} cannot be worked out from the values the run started with. {problem!.Message().Text}");
            }

            if (AccountRules.ShareHost(path) is not { } host)
            {
                return (null, $@"{path}, the path of {which}, is not a share such as \\server\share.");
            }

            (StepAccount? account, string? refused) = await ReadAsync(reference).ConfigureAwait(false);

            if (refused is not null)
            {
                return (null, refused);
            }

            if (!AccountRules.Allows(account!.Hosts, host))
            {
                return (null, $"The {account.Describe} may not connect to {host}, which the {which} names.");
            }

            connections.Add(new AgentShareConnection(path, account.UserName, account.Password));
            reads.Add($"The {account.Describe} of step {step.Name} ({stepId:D}) of {run!.Title}, for {path}.");
        }

        foreach (string detail in reads)
        {
            Audit(run!, machine, address, detail);
        }

        return (new AgentStepAccounts(runAsAccount, connections), null);
    }

    // A join with the account a step names, into that account's domain. The default organizational unit is the
    // configured domain's, so it applies only when the account's domain is that one.
    private async Task<(AgentJoinDomainCredentials? Credentials, string? Refusal)> AccountJoinAsync(
        Machine machine,
        Deployment run,
        SequenceDefinition definition,
        JoinDomainStep join,
        AccountReference reference,
        RunInputs inputs,
        string? address,
        CancellationToken cancellationToken)
    {
        (StepAccount? account, string? refused) = await AccountAsync(run, definition, reference, cancellationToken).ConfigureAwait(false);

        if (refused is not null)
        {
            return (null, refused);
        }

        if (account!.Domain is not { } domain)
        {
            return (null, $"The {account.Describe} names no domain, so it joins none.");
        }

        (string? organizationalUnit, string? unitProblem) = OrganizationalUnit(
            join,
            AccountRules.Same(domain, inputs.DomainName) ? inputs.DomainOrganizationalUnit : null,
            TemplateValues(machine, run));

        if (unitProblem is not null)
        {
            return (null, unitProblem);
        }

        Audit(run, machine, address, $"The domain join credentials of step {join.Name} ({join.Id:D}) of {run.Title}, from the {account.Describe}, for {domain}.");

        return (new AgentJoinDomainCredentials(domain, organizationalUnit, account.UserName, account.Password), null);
    }

    // The account a reference names, with its password and where it may go: a stored account as it is now, or the account
    // given for the input with the destination the input declared when it was given. The input is looked up in the run's
    // own copy of the sequence.
    private async Task<(StepAccount? Account, string? Refusal)> AccountAsync(
        Deployment run,
        SequenceDefinition definition,
        AccountReference reference,
        CancellationToken cancellationToken)
    {
        if ((reference.AccountId is null) == (reference.Input is null))
        {
            return (null, "The step names no single account: it needs a stored account or an account input.");
        }

        if (reference.AccountId is { } id)
        {
            Account? stored = await database.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false);

            if (stored is null)
            {
                return (null, "The account the step names is no longer there. Choose another account in the sequence and assign it again.");
            }

            if (stored.ProtectedPassword is null)
            {
                return (null, $"The account {stored.Name} has no password. Set it on the Accounts page.");
            }

            if (accounts.Unprotect(stored.Id, stored.ProtectedPassword) is not { } password)
            {
                return (null, $"The password of the account {stored.Name} no longer decrypts with this server's key ring. Enter it again on the Accounts page.");
            }

            return (new StepAccount(
                $"account {stored.Name} ({stored.Id:D})",
                stored.UserName,
                password,
                AccountRules.Trimmed(stored.Domain),
                AccountRules.ReadHosts(stored.Hosts),
                stored.RunAs), null);
        }

        string name = reference.Input!;
        InputDeclaration? input = definition.Inputs?.FirstOrDefault(i => i is { Kind: InputKind.Account } && AccountRules.Same(i.Name, name));

        if (input is null)
        {
            return (null, $"The run's sequence has no account input {name}.");
        }

        RunAccount? given = await credentials.ReadAsync(run.Id, input.Name, cancellationToken).ConfigureAwait(false);

        if (given is null)
        {
            return (null, $"No account was given for the input {input.Name} of this run.");
        }

        if (given.Password is null)
        {
            return (null, $"The account given for the input {input.Name} no longer decrypts with this server's key ring. Give it again.");
        }

        return (new StepAccount($"account given for the input {input.Name}", given.UserName, given.Password, given.Domain, given.Hosts, given.RunAs), null);
    }

    // The step's organizational unit worked out from the run's values, or else the default; one a value makes that the
    // domain would refuse is refused here, before the account leaves the server.
    private static (string? OrganizationalUnit, string? Problem) OrganizationalUnit(JoinDomainStep join, string? fallback, Func<string, string?> values)
    {
        if (string.IsNullOrWhiteSpace(join.OrganizationalUnit))
        {
            return (fallback, null);
        }

        string written = join.OrganizationalUnit.Trim();

        if (!ValueTemplate.TryRender(written, values, out string rendered, out TemplateProblem? problem))
        {
            return (null, $"The organizational unit {written} of step {join.Name} cannot be worked out from the run's values. {problem!.Message().Text}");
        }

        string unit = rendered.Trim();

        return DeploymentOptionsValidation.OrganizationalUnitMessage(unit) is { } refused
            ? (null, $"The organizational unit {unit} of step {join.Name} cannot be used. {refused.Text}")
            : (unit, null);
    }

    // What the answer file's and the join's templates read: the values the run started with and the variables its steps
    // set since, by name, and the machine's facts, which no value overrides but the computer name.
    private static Func<string, string?> TemplateValues(Machine machine, Deployment run)
    {
        Dictionary<string, string> values = new(StartValues(run), StringComparer.OrdinalIgnoreCase);

        foreach ((string name, string value) in RunVariables.Read(run.Variables) ?? new Dictionary<string, string>())
        {
            values[name] = value;
        }

        return (MachineVariableReader.Read(machine) with { ComputerName = null, Variables = values }).Value;
    }

    // What a share's path is made of, as the validator lets it be: the values the run started with and the machine's facts
    // as they were then, which a registration since, such as the service's in Windows, does not change. A run that
    // started before its facts were kept takes them as they are now.
    private static Func<string, string?> ShareValues(Machine machine, Deployment run, RunInputs inputs)
    {
        Func<string, string?> values = ValueTemplate.Lookup(StartValues(run));
        Func<string, string?> facts = ValueTemplate.Lookup(inputs.Facts ?? RunInputs.FactsOf(machine));

        return name => values(name) ?? facts(name);
    }

    // The values the run started with, by name ignoring case: those used, not those they overrode. Variables the agent
    // reported since are not among them, so no step can choose where an account goes.
    private static Dictionary<string, string> StartValues(Deployment run)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

        if (run.Values is null)
        {
            return values;
        }

        foreach (ResolvedValue value in JsonSerializer.Deserialize(run.Values, DdtJsonContext.Default.IReadOnlyListResolvedValue) ?? [])
        {
            if (!value.Overridden && value.Value is not null)
            {
                values.TryAdd(value.Name, value.Value);
            }
        }

        return values;
    }

    // Describe names the account for the audit and a refusal, never its password.
    private sealed record StepAccount(string Describe, string UserName, string Password, string? Domain, IReadOnlyList<string> Hosts, bool RunAs)
    {
        public override string ToString() => $"StepAccount {{ Describe = {Describe}, UserName = {UserName} }}";
    }

    private void Audit(Deployment run, Machine machine, string? address, string detail) =>
        database.AuditEvents.Add(new AuditEvent
        {
            OccurredUtc = timeProvider.GetUtcNow(),
            Action = AuditActions.DeploymentSecretRead,
            ActorMachineId = machine.Id,
            SubjectId = run.Id.ToString("D"),
            SourceAddress = address,
            Detail = StoredText.Bound(detail, AuditEvent.MaxDetailLength),
        });
}
