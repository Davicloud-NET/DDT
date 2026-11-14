// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Core.Unattend;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Sequences;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// The first-logon commands and the automatic logon of a Write answer file step. Without a Windows step they go into
// the answer file. Otherwise DDT's own session needs Winlogon until the run ends, so the agent fetches them then.
public sealed class FirstLogonSecrets(DdtDbContext database, DdtSettings settings, RunQueries queries, StepAccountLookup lookup, TimeProvider timeProvider)
{
    // Any path that goes on in Windows counts.
    public static bool InAnswerFile(SequenceDefinition definition) =>
        !SequenceChecks.ContinuesInWindows(SequenceChecks.NodePhases(definition));

    // Whether the agent fetches them as the run ends in Windows.
    public static bool AtRunEnd(SequenceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return SequenceTree.Nodes(definition).OfType<WriteUnattendStep>().Any(step => step.SetsFirstLogon) && !InAnswerFile(definition);
    }

    // The step's settings with the account's password. Audit names the account read, never its password.
    public async Task<(FirstLogon? FirstLogon, string? Refusal)> ReadAsync(
        Deployment run,
        SequenceDefinition definition,
        RunInputs inputs,
        WriteUnattendStep step,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(step);

        string[] commands = [.. (step.FirstLogonCommands ?? []).Where(command => !string.IsNullOrWhiteSpace(command)).Select(command => command.Trim())];

        switch (step.AutoLogon)
        {
            case AutoLogonKind.LocalAdministrator:
                if (!step.LocalAdministrator || settings.Current.Deployment.LocalAdministrator.Password is not { Length: > 0 } password)
                {
                    return (null, "Windows is to sign in as the local administrator, but the step adds none or DDT:Deployment:LocalAdministrator has no password.");
                }

                return (new FirstLogon(commands, new AutoLogonAccount(inputs.AdministratorName, null, password, step.AutoLogonCount), $"the local administrator {inputs.AdministratorName}"), null);

            case AutoLogonKind.Account when step.AutoLogonAccount is { } reference:
                (StepAccount? account, string? refused) = await lookup.ReadAsync(run, definition, reference, cancellationToken).ConfigureAwait(false);

                return account is null ? (null, refused) : (new FirstLogon(commands, Of(account, step.AutoLogonCount), $"the {account.Describe}"), null);

            case AutoLogonKind.Account:
                return (null, "Windows is to sign in as an account, but the step names none.");

            default:
                return (new FirstLogon(commands, null, null), null);
        }
    }

    // For the agent as the run ends in Windows: only for this machine's active run while it runs there. Empty when the
    // Write answer file step that ran sets nothing.
    public async Task<(AgentFirstLogon? FirstLogon, string? Refusal)> ForAgentAsync(
        Machine machine,
        Guid runId,
        string? address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        Deployment? run = machine.AgentEnvironment == AgentEnvironment.Windows && machine.ActiveDeploymentId == runId
            ? await database.Deployments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == runId, cancellationToken).ConfigureAwait(false)
            : null;

        if (run is not { State: DeploymentState.Running, CurrentPhase: SequencePhase.Windows, Inputs: { } inputs })
        {
            return (null, "Only this machine's run gets them, while it runs in Windows.");
        }

        SequenceDefinition definition = await queries.DefinitionAsync(run, cancellationToken).ConfigureAwait(false);

        if (InAnswerFile(definition))
        {
            return (null, "The run's answer file holds them.");
        }

        if (await RanStepAsync(run.Id, definition, cancellationToken).ConfigureAwait(false) is not { SetsFirstLogon: true } step)
        {
            return (new AgentFirstLogon([], null), null);
        }

        (FirstLogon? read, string? refusal) = await ReadAsync(run, definition, RunInputs.Read(inputs), step, cancellationToken).ConfigureAwait(false);

        if (read is null)
        {
            return (null, refusal);
        }

        if (read.Account is { } account)
        {
            database.AuditEvents.Add(RunSecrets.SecretRead(
                run,
                machine,
                address,
                timeProvider.GetUtcNow(),
                $"The automatic logon of step {step.Name} ({step.Id:D}) of {run.Title}, with {account}."));
        }

        AgentAutoLogon? autoLogon = read.AutoLogon is { } signIn ? new AgentAutoLogon(signIn.UserName, signIn.Domain, signIn.Password, signIn.Count) : null;

        return (new AgentFirstLogon(read.Commands, autoLogon), null);
    }

    // A user name with its domain in it keeps it. A UPN needs none.
    internal static AutoLogonAccount Of(StepAccount account, int count)
    {
        ArgumentNullException.ThrowIfNull(account);

        string userName = account.UserName.Trim();
        int slash = userName.IndexOf('\\', StringComparison.Ordinal);

        if (slash > 0)
        {
            return new AutoLogonAccount(userName[(slash + 1)..], userName[..slash], account.Password, count);
        }

        return new AutoLogonAccount(userName, userName.Contains('@', StringComparison.Ordinal) ? null : account.Domain, account.Password, count);
    }

    // Of several Write answer file steps on different branches, the one that ran.
    private async Task<WriteUnattendStep?> RanStepAsync(Guid runId, SequenceDefinition definition, CancellationToken cancellationToken)
    {
        WriteUnattendStep[] steps = [.. SequenceTree.Nodes(definition).OfType<WriteUnattendStep>()];
        Guid[] ids = [.. steps.Select(step => step.Id)];
        List<Guid> done = await database.DeploymentSteps
            .AsNoTracking()
            .Where(row => row.DeploymentId == runId && row.State == StepState.Done && ids.Contains(row.StepId))
            .Select(row => row.StepId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return steps.FirstOrDefault(step => done.Contains(step.Id));
    }
}
