// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Rules;
using DDT.Server.Sequences;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// Applies an agent's report to its run. A report names every step that has left Pending, so a report sent again
// changes nothing, and a step only moves forward: from Pending to Running, and on to Done, Skipped or Failed. The
// server stamps the times, because the Windows PE clock can be hours off. Nothing here saves, see DeploymentService.
public sealed class RunReports(DdtDbContext database, DdtSettings settings, RunValues values, TimeProvider timeProvider)
{
    private const string AskAgain = "Ask the server for the current run.";

    // A test's field within its step, such as test.parts[3].parts[1]: four levels of groups at most.
    private const int MaxEvaluationPathLength = 256;

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
                RunStart start = await StartAsync(machine, run, address, now, cancellationToken).ConfigureAwait(false);

                if (start.Error is { } problem)
                {
                    return DeploymentDecision.Refused(run, problem);
                }

                if (start.InputsPending is { } pending)
                {
                    // Nothing runs before the run has its values.
                    if (report.Steps.Any(step => step.State != StepState.Pending))
                    {
                        return DeploymentDecision.Conflict($"The run waits at its start for answers to its inputs, so none of its steps can have run. {AskAgain}");
                    }

                    run.CurrentPhase = report.Phase;
                    run.Activity = report.Activity;
                    run.UpdatedUtc = now;

                    return DeploymentDecision.Accepted(run) with { InputsPending = pending };
                }

                if (Progress(run, steps, report, now, lenient: false) is { } refusedStart)
                {
                    return refusedStart;
                }

                await KeepVariablesAsync(run, report, cancellationToken).ConfigureAwait(false);

                return DeploymentDecision.Accepted(run) with { Started = true };

            case (DeploymentState.Running, DeploymentState.Running):
                if (Progress(run, steps, report, now, lenient: false) is { } refusedReport)
                {
                    return refusedReport;
                }

                await KeepVariablesAsync(run, report, cancellationToken).ConfigureAwait(false);

                return DeploymentDecision.Accepted(run);

            case (DeploymentState.Running, DeploymentState.Done):
                if (Progress(run, steps, report, now, lenient: false) is { } refused)
                {
                    return refused;
                }

                if (NotDone(await DefinitionAsync(run, cancellationToken).ConfigureAwait(false), steps) is { } notDone)
                {
                    return notDone;
                }

                await KeepVariablesAsync(run, report, cancellationToken).ConfigureAwait(false);

                End(machine, run, DeploymentState.Done, null, now);
                machine.State = MachineState.Done;
                database.AuditEvents.Add(Audit(AuditActions.DeploymentDone, run, machine, now, address, $"{run.Title} on machine {machine.Id:D}."));

                return DeploymentDecision.Accepted(run);

            // A failure always ends the run, even when the steps it reports do not fit: the agent has stopped anyway.
            case (DeploymentState.Assigned or DeploymentState.Running, DeploymentState.Failed):
                Progress(run, steps, report, now, lenient: true);

                if (run.State == DeploymentState.Running)
                {
                    await KeepVariablesAsync(run, report, cancellationToken).ConfigureAwait(false);
                }

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

    // Starts an assigned run once it has its values, as its agent's first report or the answers given at the machine ask.
    // One settings snapshot for the checks and for what the run captures, so a save between them cannot start a run with
    // values nobody checked. The values are worked out from the run's answers and the rules as they are now. What keeps
    // the run from starting ends it, before any disk is touched, except a required input the machine asks: then the run
    // waits at its start, still assigned, until the machine or the machine's page answers it. Nothing here saves.
    public async Task<RunStart> StartAsync(Machine machine, Deployment run, string? address, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(run);

        SettingsSnapshot snapshot = settings.Current;
        SequenceDefinition definition = await DefinitionAsync(run, cancellationToken).ConfigureAwait(false);
        string? problem = StartProblem(snapshot, definition);

        if (problem is null)
        {
            RunValueCheck check = await values.CheckAsync(machine, run, definition, snapshot.Deployment, cancellationToken).ConfigureAwait(false);
            InputDeclaration[] webOnly = [.. check.Missing.Where(input => input.AskAt == InputAsk.Web)];

            if (webOnly.Length == 0 && check.Missing.Count > 0)
            {
                run.InputsPending = true;

                return new RunStart(null, check.AskedAtMachine);
            }

            RunInputs inputs = RunInputs.From(check.Resolution.Effective, snapshot.Deployment, now);
            problem = webOnly.Length > 0
                ? $"The run did not start, because only the web asks what it lacks: {Sentences(webOnly.Select(input => ServerMessages.ValuesInputRequired.With("label", input.Label).Text))} Assign the sequence again and answer it."
                : check.Problems.Count > 0
                    ? $"The run's values have problems, so it did not start: {Sentences(check.Problems.Select(value => value.Message.Text))}"
                    : SettingsProblem(definition, inputs);

            if (problem is null)
            {
                run.InputsPending = false;
                run.State = DeploymentState.Running;
                run.StartedUtc = now;
                run.UpdatedUtc = now;
                run.Values = RunValues.Write(check.Resolution.Values);
                run.Inputs = inputs.Write();
                machine.State = MachineState.Deploying;
                database.AuditEvents.Add(Audit(AuditActions.DeploymentStarted, run, machine, now, address, $"{run.Title} on machine {machine.Id:D}."));

                return new RunStart(null, null);
            }
        }

        string error = StoredText.Bound(problem, DeploymentLimits.MaxErrorLength)!;
        run.InputsPending = false;
        End(machine, run, DeploymentState.Failed, error, now);
        machine.State = MachineState.Failed;
        database.AuditEvents.Add(Audit(AuditActions.DeploymentFailed, run, machine, now, address, $"{run.Title} on machine {machine.Id:D} did not start: {error}"));

        return new RunStart(error, null);
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

        IReadOnlyList<SequenceStep> nodes = SequenceTree.Nodes(definition);

        if (nodes.OfType<WriteUnattendStep>().Any(s => s.LocalAdministrator) && string.IsNullOrEmpty(deployment.LocalAdministrator.Password))
        {
            return "The sequence adds the local administrator, but DDT:Deployment:LocalAdministrator has no password any more. Configure one and assign the sequence again.";
        }

        // A join that names an account joins that account's domain, and needs none of the configured one.
        return nodes.OfType<JoinDomainStep>().Any(join => join.Account is null)
            && (string.IsNullOrWhiteSpace(deployment.Domain.Name)
                || string.IsNullOrWhiteSpace(deployment.Domain.UserName)
                || string.IsNullOrEmpty(deployment.Domain.Password))
                ? "The sequence joins the domain, but DDT:Deployment:Domain no longer names a domain and an account to join it with. Configure them and assign the sequence again."
                : null;
    }

    // The settings of the answer file and the join, as the run's values give them: a rule or an input can give a time
    // zone, a locale, a keyboard or an organizational unit that the settings page would have refused. Only what the
    // sequence uses is checked; the computer name was checked with the values.
    private static string? SettingsProblem(SequenceDefinition definition, RunInputs inputs)
    {
        IReadOnlyList<SequenceStep> nodes = SequenceTree.Nodes(definition);

        if (nodes.Any(node => node is WriteUnattendStep))
        {
            if (inputs.TimeZone is { } timeZone && !WindowsSettings.IsTimeZone(timeZone))
            {
                return $"The run's {MachineValues.TimeZone} value, {timeZone}, is not a Windows time zone, so it did not start.";
            }

            if (inputs.Locale is { } locale && !WindowsSettings.IsLocale(locale))
            {
                return $"The run's {MachineValues.Locale} value, {locale}, is not a locale that names a region, so it did not start.";
            }

            if (inputs.Keyboard is { } keyboard && !WindowsSettings.IsKeyboard(keyboard))
            {
                return $"The run's {MachineValues.Keyboard} value, {keyboard}, is not a list of keyboards Windows knows, so it did not start.";
            }
        }

        return nodes.Any(node => node is JoinDomainStep)
            && inputs.DomainOrganizationalUnit is { } organizationalUnit
            && DeploymentOptionsValidation.OrganizationalUnitMessage(organizationalUnit) is { } refused
                ? $"The run's {MachineValues.OrganizationalUnit} value, {organizationalUnit}, cannot be used, so it did not start. {refused.Text}"
                : null;
    }

    private static string Sentences(IEnumerable<string> sentences) => string.Join(" ", sentences);

    private async Task<SequenceDefinition> DefinitionAsync(Deployment run, CancellationToken cancellationToken)
    {
        DeploymentSnapshot snapshot = await database.DeploymentSnapshots
            .AsNoTracking()
            .SingleAsync(s => s.DeploymentId == run.Id, cancellationToken)
            .ConfigureAwait(false);

        return SequenceDocuments.Read(snapshot.Definition);
    }

    // Null when the report fits the run. Lenient leaves a step that cannot move as reported where it is.
    //
    // A node of a tree is visited again inside a repeat, and each visit has a higher pass. A higher pass starts the new
    // visit from whatever state it reports, with its own times; within one pass a node only moves forward. Only the latest
    // visit is kept: the earlier ones are in the log. A flat run, or an agent that sends no passes, has pass 0 throughout.
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

            if (reported.Pass > step.Pass)
            {
                Visit(step, reported, report.Phase, now);

                continue;
            }

            if (reported.Pass < step.Pass)
            {
                if (lenient)
                {
                    continue;
                }

                return DeploymentDecision.Conflict(
                    $"Step {step.Index + 1}, {step.Name}, is in its visit {step.Pass} and cannot go back to visit {reported.Pass}. {AskAgain}");
            }

            if (reported.State == step.State)
            {
                Decided(step, reported);

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

            // Where a node runs can depend on the path, so it runs in the phase the agent is in when it starts it.
            if (reported.State == StepState.Running)
            {
                step.Phase = report.Phase;
            }

            step.State = reported.State;
            step.FinishedUtc = reported.State == StepState.Running ? null : now;
            step.Percent = reported.State == StepState.Done ? 100 : step.Percent;
            step.Error = StoredText.Bound(reported.Error, DeploymentLimits.MaxErrorLength);
            Decided(step, reported);
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
            run.CurrentStepIndex = current is null ? null : StepNumber(steps, current);
            run.CurrentStepName = current?.Name;
        }

        run.Percent = percent;
        run.CurrentPhase = report.Phase;
        run.Activity = report.Activity;
        run.UpdatedUtc = now;
        Waits(run, byId, current, report);

        return null;
    }

    // The Pause step the run waits at, as the report names it: its current step, running, with the message the agent
    // worked out. A report of anything else ends the wait. A continue someone gave is kept until the visit it continued is
    // over, since the agent honours it with any report's answer until then.
    private static void Waits(Deployment run, Dictionary<Guid, DeploymentStep> byId, DeploymentStep? current, AgentRunReport report)
    {
        bool paused = report.Activity == RunActivity.Paused && current is { State: StepState.Running } && current.Kind == RunSnapshots.PauseKind;

        run.PauseStepId = paused ? current!.StepId : null;
        run.PausePass = paused ? current!.Pass : null;
        run.PauseMessage = paused ? StoredText.Bound(report.PauseMessage, DeploymentLimits.MaxPauseMessageLength) : null;

        if (run.ContinueStepId is { } continued
            && !(byId.GetValueOrDefault(continued) is { State: StepState.Running } visit && visit.Pass == run.ContinuePass))
        {
            run.ContinueStepId = null;
            run.ContinuePass = null;
            run.ContinuedByName = null;
        }
    }

    // The variables the agent sends when they changed, merged into those the run has; an Account input's name never among
    // them. A report without them leaves them as they are.
    private async Task KeepVariablesAsync(Deployment run, AgentRunReport report, CancellationToken cancellationToken)
    {
        if (report.Variables is null)
        {
            return;
        }

        SequenceDefinition definition = await DefinitionAsync(run, cancellationToken).ConfigureAwait(false);

        if (RunVariables.Merged(run.Variables, report.Variables, definition) is { } merged)
        {
            run.Variables = merged;
        }
    }

    // Whether the report changed the variables of the run, which a save is about to store. The save forgets it.
    public static bool VariablesChanged(DdtDbContext database, Deployment run)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(run);

        return database.Entry(run).Property(d => d.Variables).IsModified;
    }

    // A new visit of the node, as the report has it, with the server's times.
    private static void Visit(DeploymentStep step, StepRunState reported, SequencePhase phase, DateTimeOffset now)
    {
        step.Pass = reported.Pass;
        step.State = reported.State;
        step.StartedUtc = reported.State is StepState.Pending or StepState.Skipped ? null : now;
        step.FinishedUtc = reported.State is StepState.Pending or StepState.Running ? null : now;
        step.Percent = reported.State == StepState.Done ? 100 : 0;
        step.Error = StoredText.Bound(reported.Error, DeploymentLimits.MaxErrorLength);

        if (reported.State == StepState.Running)
        {
            step.Phase = phase;
        }

        step.Iteration = 0;
        step.Branch = null;
        step.Evaluation = null;
        Decided(step, reported);
    }

    // What the visit decided so far: a repeat's time through its body, an IF's branch and the tests behind either. They
    // come from outside, so the tests are held to the bounds the engine keeps.
    private static void Decided(DeploymentStep step, StepRunState reported)
    {
        step.Iteration = Math.Max(step.Iteration, reported.Iteration);
        step.Branch = reported.Branch ?? step.Branch;

        if (reported.Evaluation is { } evaluation)
        {
            step.Evaluation = DeploymentSummaries.WriteEvaluation(
            [
                .. evaluation
                    .OfType<TestEvaluation>()
                    .Take(TestEvaluation.MaxPerNode)
                    .Select(test => new TestEvaluation(
                        StoredText.Bound(test.Path, MaxEvaluationPathLength) ?? "",
                        test.Held,
                        test.Actual is null ? null : Cut(test.Actual, TestEvaluation.MaxActualLength))),
            ]);
        }
    }

    // A run is done when no node is running, every node the run reached ran or was skipped, and every failure was one a
    // node allowed: the failed node itself or a container it is in continues on error. A node was not reached when a
    // container it is in was skipped or failed, or an IF above it took the other branch. A flat run has no such node, so
    // every step of it must have left Pending, as before.
    private static DeploymentDecision? NotDone(SequenceDefinition definition, List<DeploymentStep> steps)
    {
        IReadOnlyDictionary<Guid, NodePosition> positions = SequenceTree.Index(definition);
        Dictionary<Guid, DeploymentStep> byId = steps.ToDictionary(s => s.StepId);

        IEnumerable<NodePosition> Up(DeploymentStep step)
        {
            for (NodePosition? at = positions.GetValueOrDefault(step.StepId); at is not null; at = at.ParentId is { } parentId ? positions.GetValueOrDefault(parentId) : null)
            {
                yield return at;
            }
        }

        bool Reached(DeploymentStep step) => !Up(step).Any(at =>
            at.ParentId is { } parentId
            && byId.GetValueOrDefault(parentId) is { } parent
            && (parent.State is StepState.Skipped or StepState.Failed
                || (positions[parentId].Step is IfStep && parent.Branch is { } branch && at.Body != BodyOf(branch))));

        bool MayFail(DeploymentStep step) => Up(step).Any(at => at.Step.ContinueOnError);

        if (steps.FirstOrDefault(s => s.State == StepState.Running || (s.State == StepState.Pending && Reached(s))) is { } open)
        {
            return DeploymentDecision.Conflict(
                $"Step {open.Index + 1}, {open.Name}, is {Word(open.State)}, so the run is not done. Report every step that ran, then Done.");
        }

        return steps.FirstOrDefault(s => s.State == StepState.Failed && !MayFail(s)) is { } failed
            ? DeploymentDecision.Conflict(
                $"Step {failed.Index + 1}, {failed.Name}, failed and must not fail, so the run is not done. Report the run as failed.")
            : null;
    }

    private static string BodyOf(IfBranch branch) => branch == IfBranch.Then ? StepBody.ThenName : StepBody.ElseName;

    // Without a NUL, which PostgreSQL text cannot hold, and cut rather than refused, like an error.
    private static string Cut(string text, int maxLength)
    {
        string kept = text.Replace("\0", string.Empty, StringComparison.Ordinal);

        return kept.Length <= maxLength ? kept : kept[..maxLength];
    }

    // The steps before the node, as StepCount counts them: a group, an IF or a repeat is not a step. For a flat run it is
    // the row's Index.
    private static int StepNumber(List<DeploymentStep> steps, DeploymentStep node) =>
        steps.Count(step => step.Index < node.Index && !RunSnapshots.IsContainer(step));

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

// Error is why the run could not start, which ended it; InputsPending the inputs the machine asks while the run waits at
// its start for a required one. Neither: the run started.
public sealed record RunStart(string? Error, IReadOnlyList<AgentInput>? InputsPending);
