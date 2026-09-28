// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Core.Templates;
using DDT.Server.Accounts;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// The answer file and the join credentials a run needs, handed to its agent only while their step runs, and every read
// audited. The caller saves the audit rows before it answers.
public sealed class RunSecrets(
    DdtDbContext database,
    UnattendRenderer renderer,
    DdtSettings settings,
    RunQueries queries,
    StepAccountLookup lookup,
    TimeProvider timeProvider)
{
    public async Task<(string? AnswerFile, string? Refusal)> AnswerFileAsync(
        Machine machine,
        Guid runId,
        Guid stepId,
        string? address,
        CancellationToken cancellationToken)
    {
        (RunningStep? running, string? refusal) = await queries.RunningStepAsync(machine, runId, stepId, cancellationToken).ConfigureAwait(false);

        if (running is null)
        {
            return (null, refusal);
        }

        if (running.Step is not WriteUnattendStep unattend)
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

        (string? answerFile, string? problem) = renderer.Render(running.Inputs, unattend, language, password, TemplateValues(machine, running.Run));

        if (problem is not null)
        {
            return (null, problem);
        }

        Audit(running.Run, machine, address, $"The answer file of step {unattend.Name} ({stepId:D}) of {running.Run.Title}.");

        return (answerFile, null);
    }

    // The configured domain the run started with: a domain named elsewhere could send the join account to a foreign
    // controller. A step that names an account joins that account's. Joins run in Windows, so only the service there gets it.
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

        (RunningStep? running, string? refusal) = await queries.RunningStepAsync(machine, runId, stepId, cancellationToken).ConfigureAwait(false);

        if (running is null)
        {
            return (null, refusal);
        }

        if (running.Step is not JoinDomainStep join)
        {
            return (null, "That step joins no domain.");
        }

        if (join.Account is { } named)
        {
            (StepAccount? account, string? refused) = await lookup.ReadAsync(running.Run, running.Definition, named, cancellationToken).ConfigureAwait(false);

            return account is null ? (null, refused) : AccountJoin(machine, running, join, account, address);
        }

        DomainOptions domain = settings.Current.Deployment.Domain;

        if (string.IsNullOrWhiteSpace(domain.UserName) || string.IsNullOrEmpty(domain.Password))
        {
            return (null, "DDT:Deployment:Domain no longer names an account to join the domain with.");
        }

        if (running.Inputs.DomainName is not { } name || !string.Equals(name, domain.Name?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return (null, "The configured domain changed after the run started, so its account is not for this run. Assign the sequence again.");
        }

        (string? organizationalUnit, string? unitProblem) = OrganizationalUnit(join, running.Inputs.DomainOrganizationalUnit, TemplateValues(machine, running.Run));

        if (unitProblem is not null)
        {
            return (null, unitProblem);
        }

        Audit(running.Run, machine, address, $"The domain join credentials of step {join.Name} ({stepId:D}) of {running.Run.Title}, for {name}.");

        return (new AgentJoinDomainCredentials(name, organizationalUnit, domain.UserName.Trim(), domain.Password), null);
    }

    // A read of a password or an answer file, audited by what was read, never the secret.
    internal static AuditEvent SecretRead(Deployment run, Machine machine, string? address, DateTimeOffset now, string detail) =>
        AuditEvents.Create(AuditActions.DeploymentSecretRead, run.Id.ToString("D"), Actor.OfMachine(machine.Id, address), now, detail);

    // The default organizational unit is the configured domain's, so it applies only when the account's domain is that one.
    private (AgentJoinDomainCredentials? Credentials, string? Refusal) AccountJoin(
        Machine machine,
        RunningStep running,
        JoinDomainStep join,
        StepAccount account,
        string? address)
    {
        if (account.Domain is not { } domain)
        {
            return (null, $"The {account.Describe} names no domain, so it joins none.");
        }

        (string? organizationalUnit, string? unitProblem) = OrganizationalUnit(
            join,
            AccountRules.Same(domain, running.Inputs.DomainName) ? running.Inputs.DomainOrganizationalUnit : null,
            TemplateValues(machine, running.Run));

        if (unitProblem is not null)
        {
            return (null, unitProblem);
        }

        Audit(running.Run, machine, address, $"The domain join credentials of step {join.Name} ({join.Id:D}) of {running.Run.Title}, from the {account.Describe}, for {domain}.");

        return (new AgentJoinDomainCredentials(domain, organizationalUnit, account.UserName, account.Password), null);
    }

    // The step's organizational unit worked out from the run's values, or else the default. One the domain would refuse
    // is refused here, before the account leaves the server.
    private static (string? OrganizationalUnit, string? Problem) OrganizationalUnit(JoinDomainStep join, string? fallback, Func<string, string?> values)
    {
        if (string.IsNullOrWhiteSpace(join.OrganizationalUnit))
        {
            return (fallback, null);
        }

        string written = join.OrganizationalUnit.Trim();

        if (!ValueTemplate.TryRender(written, values, out string rendered, out TemplateProblem? problem))
        {
            return (null, $"The organizational unit {written} of step {join.Name} cannot be worked out from the run's values. {problem?.Message().Text}");
        }

        string unit = rendered.Trim();

        return DeploymentOptionsValidation.OrganizationalUnitMessage(unit) is { } refused
            ? (null, $"The organizational unit {unit} of step {join.Name} cannot be used. {refused.Text}")
            : (unit, null);
    }

    // The values the run started with and the variables its steps set since, by name, and the machine's facts, which no
    // value overrides but the computer name.
    private static Func<string, string?> TemplateValues(Machine machine, Deployment run)
    {
        Dictionary<string, string> values = new(RunValues.Effective(run) ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);

        foreach ((string name, string value) in RunVariables.Read(run.Variables) ?? new Dictionary<string, string>())
        {
            values[name] = value;
        }

        return (MachineVariableReader.Read(machine) with { ComputerName = null, Variables = values }).Value;
    }

    private void Audit(Deployment run, Machine machine, string? address, string detail) =>
        database.AuditEvents.Add(SecretRead(run, machine, address, timeProvider.GetUtcNow(), detail));
}
