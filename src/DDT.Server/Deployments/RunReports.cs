// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Sequences;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// Applies an agent's report to its run. A report names every step that has left Pending, so a report sent again changes
// nothing. The server stamps the times, since the Windows PE clock can be hours off. Nothing here saves.
public sealed class RunReports(DdtDbContext database, RunQueries queries, RunStarts starts, TimeProvider timeProvider)
{
    public async Task<DeploymentDecision> ApplyAsync(
        Machine machine,
        Guid runId,
        AgentRunReport report,
        string? address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(report);

        (Deployment? run, DeploymentDecision? refusal) = await GuardAsync(machine, runId, report, cancellationToken).ConfigureAwait(false);

        if (run is null)
        {
            return refusal ?? DeploymentDecision.NotFound($"This machine has no such run. {RunProgress.AskAgain}");
        }

        List<DeploymentStep> steps = await database.DeploymentSteps
            .Where(s => s.DeploymentId == run.Id)
            .OrderBy(s => s.Index)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // A tree's path depends on its IFs, which only the definition says how to follow.
        SequenceDefinition? tree = steps.Any(RunSnapshots.IsContainer) ? await queries.DefinitionAsync(run, cancellationToken).ConfigureAwait(false) : null;
        ReceivedReport received = new(machine, run, report, steps, tree, address, timeProvider.GetUtcNow());

        return (run.State, report.State) switch
        {
            (DeploymentState.Assigned, DeploymentState.Running) => await StartedAsync(received, cancellationToken).ConfigureAwait(false),
            (DeploymentState.Running, DeploymentState.Running) => await ProgressedAsync(received, cancellationToken).ConfigureAwait(false),
            (DeploymentState.Running, DeploymentState.Done) => await DoneAsync(received, cancellationToken).ConfigureAwait(false),

            // A failure always ends the run, even when the steps it reports do not fit: the agent has stopped anyway.
            (DeploymentState.Assigned or DeploymentState.Running, DeploymentState.Failed) => await FailedAsync(received, cancellationToken).ConfigureAwait(false),
            _ => DeploymentDecision.Conflict(
                $"A run that is {RunProgress.Word(run.State)} cannot be reported as {RunProgress.Word(report.State)}. {RunProgress.AskAgain}"),
        };
    }

    // A run that ends while a step runs ends that step with it.
    public static void FailRunning(IEnumerable<DeploymentStep> steps, string error, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(steps);

        foreach (DeploymentStep step in steps.Where(s => s.State == StepState.Running))
        {
            step.State = StepState.Failed;
            step.FinishedUtc = now;
            step.Error = StoredText.Bound(error, DeploymentLimits.MaxErrorLength);
        }
    }

    // The steps a change that is about to be saved moves on. The save forgets which they were.
    public static IReadOnlyList<DeploymentStep> ChangedSteps(DdtDbContext database)
    {
        ArgumentNullException.ThrowIfNull(database);

        return [.. database.ChangeTracker.Entries<DeploymentStep>().Where(e => e.State == EntityState.Modified).Select(e => e.Entity)];
    }

    // Whether the report changed the variables of the run, which a save is about to store. The save forgets it.
    public static bool VariablesChanged(DdtDbContext database, Deployment run)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(run);

        return database.Entry(run).Property(d => d.Variables).IsModified;
    }

    // The run the report may change, or why it may not. Null for both: the machine has no such run.
    private async Task<(Deployment? Run, DeploymentDecision? Refusal)> GuardAsync(
        Machine machine,
        Guid runId,
        AgentRunReport report,
        CancellationToken cancellationToken)
    {
        if (Malformation(report) is { } malformed)
        {
            return (null, DeploymentDecision.Invalid("report", malformed));
        }

        Deployment? run = await database.Deployments.FindAsync([runId], cancellationToken).ConfigureAwait(false);

        if (run is null || run.MachineId != machine.Id)
        {
            return (null, null);
        }

        // The response to the last report can be lost, and the agent sends it again.
        if (run.State == report.State && run.State is DeploymentState.Done or DeploymentState.Failed)
        {
            return (null, DeploymentDecision.Unchanged(run));
        }

        if (machine.ActiveDeploymentId != run.Id)
        {
            return (null, DeploymentDecision.Conflict($"The run is {RunProgress.Word(run.State)} and takes no further reports. {RunProgress.AskAgain}"));
        }

        // The service in Windows cannot take the run back to the Windows PE phase: Windows PE registers again before it
        // does, as when the machine started it instead of the installed Windows. A failure ends the run in any phase.
        return report.State != DeploymentState.Failed
            && report.Phase == SequencePhase.WindowsPE
            && machine.AgentEnvironment == AgentEnvironment.Windows
                ? (null, DeploymentDecision.Conflict(
                    $"This machine registered from Windows, so its run cannot be in the Windows PE phase. An agent in Windows PE registers before it reports. {RunProgress.AskAgain}"))
                : (run, null);
    }

    // Everything here comes from the agent, so an old agent or a forged report is refused before it touches a row.
    private static string? Malformation(AgentRunReport report)
    {
        if (report.State is not (DeploymentState.Running or DeploymentState.Done or DeploymentState.Failed))
        {
            return "A report says the run is Running, Done or Failed.";
        }

        if (!Enum.IsDefined(report.Phase) || !Enum.IsDefined(report.Activity))
        {
            return "The report names a phase or an activity this server does not know. Use the agent this server provides.";
        }

        if (report.Steps is null || report.Steps.Count > SequenceLimits.MaxStoredNodes || report.Steps.Any(s => s is null || !Enum.IsDefined(s.State)))
        {
            return "The report's steps are missing, too many, or in a state this server does not know.";
        }

        if (report.Steps.Any(s => s.Pass < 0 || s.Iteration < 0 || (s.Branch is { } branch && !Enum.IsDefined(branch))))
        {
            return "The report names a visit, a time through a repeat or a branch that cannot be.";
        }

        if (report.Variables is { Count: > RunVariables.MaxCount })
        {
            return $"The report holds more than {RunVariables.MaxCount} variables.";
        }

        return report.Steps.Select(s => s.StepId).Distinct().Count() == report.Steps.Count
            ? null
            : "The report names a step twice.";
    }

    private async Task<DeploymentDecision> StartedAsync(ReceivedReport received, CancellationToken cancellationToken)
    {
        (Machine machine, Deployment run, AgentRunReport report) = (received.Machine, received.Run, received.Report);
        RunStart start = await starts.StartAsync(machine, run, received.Address, received.Now, cancellationToken).ConfigureAwait(false);

        if (start.Error is { } problem)
        {
            return DeploymentDecision.Refused(run, problem);
        }

        if (start.InputsPending is { } pending)
        {
            // Nothing runs before the run has its values.
            if (report.Steps.Any(step => step.State != StepState.Pending))
            {
                return DeploymentDecision.Conflict($"The run waits at its start for answers to its inputs, so none of its steps can have run. {RunProgress.AskAgain}");
            }

            run.CurrentPhase = report.Phase;
            run.Activity = report.Activity;
            run.UpdatedUtc = received.Now;

            return DeploymentDecision.Accepted(run) with { InputsPending = pending };
        }

        if (RunProgress.Apply(received, lenient: false) is { } refused)
        {
            return refused;
        }

        await KeepVariablesAsync(run, report, cancellationToken).ConfigureAwait(false);

        return DeploymentDecision.Accepted(run) with { Started = true };
    }

    private async Task<DeploymentDecision> ProgressedAsync(ReceivedReport received, CancellationToken cancellationToken)
    {
        if (RunProgress.Apply(received, lenient: false) is { } refused)
        {
            return refused;
        }

        await KeepVariablesAsync(received.Run, received.Report, cancellationToken).ConfigureAwait(false);

        return DeploymentDecision.Accepted(received.Run);
    }

    private async Task<DeploymentDecision> DoneAsync(ReceivedReport received, CancellationToken cancellationToken)
    {
        (Machine machine, Deployment run) = (received.Machine, received.Run);

        if (RunProgress.Apply(received, lenient: false) is { } refused)
        {
            return refused;
        }

        if (RunProgress.NotDone(await queries.DefinitionAsync(run, cancellationToken).ConfigureAwait(false), received.Steps) is { } notDone)
        {
            return notDone;
        }

        await KeepVariablesAsync(run, received.Report, cancellationToken).ConfigureAwait(false);

        RunTermination.End(machine, run, DeploymentState.Done, null, received.Now);
        machine.State = MachineState.Done;
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.DeploymentDone,
            run.Id.ToString("D"),
            Actor.OfMachine(machine.Id, received.Address),
            received.Now,
            $"{run.Title} on machine {machine.Id:D}."));

        return DeploymentDecision.Accepted(run);
    }

    private async Task<DeploymentDecision> FailedAsync(ReceivedReport received, CancellationToken cancellationToken)
    {
        (Machine machine, Deployment run) = (received.Machine, received.Run);
        RunProgress.Apply(received, lenient: true);

        if (run.State == DeploymentState.Running)
        {
            await KeepVariablesAsync(run, received.Report, cancellationToken).ConfigureAwait(false);
        }

        string error = StoredText.Bound(received.Report.Error, DeploymentLimits.MaxErrorLength) ?? "The agent reported a failure without saying why.";
        FailRunning(received.Steps, error, received.Now);
        RunTermination.End(machine, run, DeploymentState.Failed, error, received.Now);
        machine.State = MachineState.Failed;
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.DeploymentFailed,
            run.Id.ToString("D"),
            Actor.OfMachine(machine.Id, received.Address),
            received.Now,
            run.CurrentStepName is { } step
                ? $"{run.Title} on machine {machine.Id:D} at {step}: {error}"
                : $"{run.Title} on machine {machine.Id:D}: {error}"));

        return DeploymentDecision.Accepted(run);
    }

    // The variables the agent sends when they changed, merged into those the run has; an Account input's name is never
    // among them. A report without them leaves them as they are.
    private async Task KeepVariablesAsync(Deployment run, AgentRunReport report, CancellationToken cancellationToken)
    {
        if (report.Variables is null)
        {
            return;
        }

        SequenceDefinition definition = await queries.DefinitionAsync(run, cancellationToken).ConfigureAwait(false);

        if (RunVariables.Merged(run.Variables, report.Variables, definition) is { } merged)
        {
            run.Variables = merged;
        }
    }
}
