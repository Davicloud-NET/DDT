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
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// Applies an agent's report to its run. A report names every step that has left Pending, so a report sent again
// changes nothing, and a step only moves forward: from Pending to Running, and on to Done, Skipped or Failed. The
// server stamps the times, because the Windows PE clock can be hours off. Nothing here saves, see DeploymentService.
public sealed class RunReports(DdtDbContext database, DdtSettings settings, TimeProvider timeProvider)
{
    private const string AskAgain = "Ask the server for the current run.";

    public async Task<DeploymentDecision> ApplyAsync(
        Machine machine,
        Guid runId,
        AgentRunReport report,
        string? address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(report);

        if (Malformation(report) is { } malformed)
        {
            return DeploymentDecision.Invalid("report", malformed);
        }

        Deployment? run = await database.Deployments.FindAsync([runId], cancellationToken).ConfigureAwait(false);

        if (run is null || run.MachineId != machine.Id)
        {
            return DeploymentDecision.NotFound($"This machine has no such run. {AskAgain}");
        }

        // The response to the last report can be lost, and the agent sends it again.
        if (run.State == report.State && run.State is DeploymentState.Done or DeploymentState.Failed)
        {
            return DeploymentDecision.Unchanged(run);
        }

        if (machine.ActiveDeploymentId != run.Id)
        {
            return DeploymentDecision.Conflict($"The run is {Word(run.State)} and takes no further reports. {AskAgain}");
        }

        // The service in Windows cannot take the run back to the Windows PE phase. Windows PE registers again before it
        // does, as when the machine started it instead of the installed Windows and it hands the run over again. A
        // failure still ends the run, whatever phase it names.
        if (report.State != DeploymentState.Failed
            && report.Phase == SequencePhase.WindowsPE
            && machine.AgentEnvironment == AgentEnvironment.Windows)
        {
            return DeploymentDecision.Conflict(
                $"This machine registered from Windows, so its run cannot be in the Windows PE phase. An agent in Windows PE registers before it reports. {AskAgain}");
        }

        List<DeploymentStep> steps = await database.DeploymentSteps
            .Where(s => s.DeploymentId == run.Id)
            .OrderBy(s => s.Index)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = timeProvider.GetUtcNow();

        switch (run.State, report.State)
        {
            case (DeploymentState.Assigned, DeploymentState.Running):
                // One snapshot for the check and for what the run captures, so a save between them cannot start a run
                // with values nobody checked.
                SettingsSnapshot snapshot = settings.Current;

                if (StartProblem(snapshot, await DefinitionAsync(run, cancellationToken).ConfigureAwait(false)) is { } problem)
                {
                    End(machine, run, DeploymentState.Failed, problem, now);
                    machine.State = MachineState.Failed;
                    database.AuditEvents.Add(Audit(AuditActions.DeploymentFailed, run, machine, now, address, $"{run.Title} on machine {machine.Id:D} did not start: {problem}"));

                    return DeploymentDecision.Refused(run, problem);
                }

                run.State = DeploymentState.Running;
                run.StartedUtc = now;
                run.Inputs = RunInputs.Capture(machine, snapshot.Deployment, now).Write();
                machine.State = MachineState.Deploying;
                database.AuditEvents.Add(Audit(AuditActions.DeploymentStarted, run, machine, now, address, $"{run.Title} on machine {machine.Id:D}."));

                return Progress(run, steps, report, now, lenient: false) ?? DeploymentDecision.Accepted(run);

            case (DeploymentState.Running, DeploymentState.Running):
                return Progress(run, steps, report, now, lenient: false) ?? DeploymentDecision.Accepted(run);

            case (DeploymentState.Running, DeploymentState.Done):
                if (Progress(run, steps, report, now, lenient: false) is { } refused)
                {
                    return refused;
                }

                if (steps.FirstOrDefault(s => s.State is StepState.Pending or StepState.Running) is { } open)
                {
                    return DeploymentDecision.Conflict(
                        $"Step {open.Index + 1}, {open.Name}, is {Word(open.State)}, so the run is not done. Report every step that ran, then Done.");
                }

                // A failed step ends the run, unless it may fail.
                HashSet<Guid> mayFail = [.. (await DefinitionAsync(run, cancellationToken).ConfigureAwait(false)).Steps.Where(s => s.ContinueOnError).Select(s => s.Id)];

                if (steps.FirstOrDefault(s => s.State == StepState.Failed && !mayFail.Contains(s.StepId)) is { } failed)
                {
                    return DeploymentDecision.Conflict(
                        $"Step {failed.Index + 1}, {failed.Name}, failed and must not fail, so the run is not done. Report the run as failed.");
                }

                End(machine, run, DeploymentState.Done, null, now);
                machine.State = MachineState.Done;
                database.AuditEvents.Add(Audit(AuditActions.DeploymentDone, run, machine, now, address, $"{run.Title} on machine {machine.Id:D}."));

                return DeploymentDecision.Accepted(run);

            // A failure always ends the run, even when the steps it reports do not fit: the agent has stopped anyway.
            case (DeploymentState.Assigned or DeploymentState.Running, DeploymentState.Failed):
                Progress(run, steps, report, now, lenient: true);

                string error = StoredText.Bound(report.Error, DeploymentLimits.MaxErrorLength) ?? "The agent reported a failure without saying why.";
                FailRunning(steps, error, now);
                End(machine, run, DeploymentState.Failed, error, now);
                machine.State = MachineState.Failed;
                database.AuditEvents.Add(Audit(
                    AuditActions.DeploymentFailed,
                    run,
                    machine,
                    now,
                    address,
                    run.CurrentStepName is { } step
                        ? $"{run.Title} on machine {machine.Id:D} at {step}: {error}"
                        : $"{run.Title} on machine {machine.Id:D}: {error}"));

                return DeploymentDecision.Accepted(run);

            default:
                return DeploymentDecision.Conflict($"A run that is {Word(run.State)} cannot be reported as {Word(report.State)}. {AskAgain}");
        }
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

        return report.Steps.Select(s => s.StepId).Distinct().Count() == report.Steps.Count
            ? null
            : "The report names a step twice.";
    }

    // The settings a run needs can go between its assignment and its start, with a restart of the server. The run
    // then fails at once rather than when the agent asks for them, halfway through.
    private static string? StartProblem(SettingsSnapshot snapshot, SequenceDefinition definition)
    {
        // A run's error is the agent's language, English, like every other run error.
        if (DeploymentService.SettingsProblem(snapshot) is { } closed)
        {
            return closed.Text;
        }

        DeploymentOptions deployment = snapshot.Deployment;

        if (definition.Steps.OfType<WriteUnattendStep>().Any(s => s.LocalAdministrator) && string.IsNullOrEmpty(deployment.LocalAdministrator.Password))
        {
            return "The sequence adds the local administrator, but DDT:Deployment:LocalAdministrator has no password any more. Configure one and assign the sequence again.";
        }

        return definition.Steps.OfType<JoinDomainStep>().Any()
            && (string.IsNullOrWhiteSpace(deployment.Domain.Name)
                || string.IsNullOrWhiteSpace(deployment.Domain.UserName)
                || string.IsNullOrEmpty(deployment.Domain.Password))
                ? "The sequence joins the domain, but DDT:Deployment:Domain no longer names a domain and an account to join it with. Configure them and assign the sequence again."
                : null;
    }

    private async Task<SequenceDefinition> DefinitionAsync(Deployment run, CancellationToken cancellationToken)
    {
        DeploymentSnapshot snapshot = await database.DeploymentSnapshots
            .AsNoTracking()
            .SingleAsync(s => s.DeploymentId == run.Id, cancellationToken)
            .ConfigureAwait(false);

        return SequenceDocuments.Read(snapshot.Definition);
    }

    // Null when the report fits the run. Lenient leaves a step that cannot move as reported where it is.
    private static DeploymentDecision? Progress(
        Deployment run,
        List<DeploymentStep> steps,
        AgentRunReport report,
        DateTimeOffset now,
        bool lenient)
    {
        Dictionary<Guid, DeploymentStep> byId = steps.ToDictionary(s => s.StepId);

        foreach (StepRunState reported in report.Steps)
        {
            if (!byId.TryGetValue(reported.StepId, out DeploymentStep? step))
            {
                if (lenient)
                {
                    continue;
                }

                return DeploymentDecision.Invalid("steps", $"The report names step {reported.StepId:D}, which the run does not have. {AskAgain}");
            }

            if (reported.State == step.State)
            {
                continue;
            }

            if (step.State is StepState.Done or StepState.Skipped or StepState.Failed || reported.State == StepState.Pending)
            {
                if (lenient)
                {
                    continue;
                }

                return DeploymentDecision.Conflict(
                    $"Step {step.Index + 1}, {step.Name}, is {Word(step.State)} and cannot become {Word(reported.State)}. {AskAgain}");
            }

            if (step.State == StepState.Pending && reported.State != StepState.Skipped)
            {
                step.StartedUtc = now;
            }

            step.State = reported.State;
            step.FinishedUtc = reported.State == StepState.Running ? null : now;
            step.Percent = reported.State == StepState.Done ? 100 : step.Percent;
            step.Error = StoredText.Bound(reported.Error, DeploymentLimits.MaxErrorLength);
        }

        DeploymentStep? current = report.CurrentStepId is { } currentId ? byId.GetValueOrDefault(currentId) : null;

        if (report.CurrentStepId is not null && current is null && !lenient)
        {
            return DeploymentDecision.Invalid("currentStepId", $"The report names a current step the run does not have. {AskAgain}");
        }

        int percent = Math.Clamp(report.Percent, 0, 100);

        if (current is { State: StepState.Running })
        {
            current.Percent = percent;
        }

        // A failure names no current step, and the run keeps showing the step it failed at.
        if (current is not null || !lenient)
        {
            run.CurrentStepIndex = current?.Index;
            run.CurrentStepName = current?.Name;
        }

        run.Percent = percent;
        run.CurrentPhase = report.Phase;
        run.Activity = report.Activity;
        run.UpdatedUtc = now;

        return null;
    }

    private static void End(Machine machine, Deployment run, DeploymentState state, string? error, DateTimeOffset now)
    {
        run.State = state;
        run.Error = error;
        run.FinishedUtc = now;
        run.UpdatedUtc = now;
        machine.ActiveDeploymentId = null;
    }

    private static string Word<T>(T value)
        where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static AuditEvent Audit(string action, Deployment run, Machine machine, DateTimeOffset now, string? address, string detail) => new()
    {
        OccurredUtc = now,
        Action = action,
        ActorMachineId = machine.Id,
        SubjectId = run.Id.ToString("D"),
        SourceAddress = address,
        Detail = StoredText.Bound(detail, AuditEvent.MaxDetailLength),
    };
}
