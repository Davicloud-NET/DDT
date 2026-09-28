// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Data;

namespace DDT.Server.Deployments;

// Moves a run's rows as a report says. Each visit of a node in a repeat has a higher pass, which starts it anew; within a
// pass a node only moves forward. Only the latest visit is kept, the earlier ones are in the log.
internal static class RunProgress
{
    public const string AskAgain = "Ask the server for the current run.";

    // A test's field within its step, such as test.parts[3].parts[1]: four levels of groups at most.
    private const int MaxEvaluationPathLength = 256;

    // Null when the report fits the run. Lenient leaves a step that cannot move as reported where it is.
    public static DeploymentDecision? Apply(ReceivedReport received, bool lenient)
    {
        (Deployment run, AgentRunReport report) = (received.Run, received.Report);
        Dictionary<Guid, DeploymentStep> byId = received.Steps.ToDictionary(s => s.StepId);

        foreach (StepRunState reported in report.Steps)
        {
            if (Move(byId.GetValueOrDefault(reported.StepId), reported, report.Phase, received.Now, lenient) is { } refused)
            {
                return refused;
            }
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

        // The steps a list counts are those of the path, which grows and shrinks as IFs decide.
        IReadOnlyList<Guid> path = RunPaths.Leaves(received.Tree, received.Steps);
        run.StepCount = path.Count;

        // A failure names no current step, and the run keeps showing the step it failed at.
        if (current is not null || !lenient)
        {
            run.CurrentStepIndex = current is null ? null : RunPaths.Number(path, received.Steps, current);
            run.CurrentStepName = current?.Name;
        }

        run.Percent = percent;
        run.CurrentPhase = report.Phase;
        run.Activity = report.Activity;
        run.UpdatedUtc = received.Now;
        Waits(run, byId, current, report);

        return null;
    }

    // A run is done when no node runs, every node the run reached ran or was skipped, and every failure was allowed by
    // ContinueOnError on the node or a container it is in. A node was not reached when a container it is in was skipped
    // or failed, or an IF above it took the other branch.
    public static DeploymentDecision? NotDone(SequenceDefinition definition, List<DeploymentStep> steps)
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

    public static string Word<T>(T value)
        where T : struct, Enum => value.ToString().ToLowerInvariant();

    // Null when the step moved, or when lenient left it where it is.
    private static DeploymentDecision? Move(DeploymentStep? step, StepRunState reported, SequencePhase phase, DateTimeOffset now, bool lenient)
    {
        if (step is null)
        {
            return lenient ? null : DeploymentDecision.Invalid("steps", $"The report names step {reported.StepId:D}, which the run does not have. {AskAgain}");
        }

        if (reported.Pass > step.Pass)
        {
            Visit(step, reported, phase, now);

            return null;
        }

        if (reported.Pass < step.Pass)
        {
            return lenient
                ? null
                : DeploymentDecision.Conflict(
                    $"Step {step.Index + 1}, {step.Name}, is in its visit {step.Pass} and cannot go back to visit {reported.Pass}. {AskAgain}");
        }

        if (reported.State == step.State)
        {
            Decided(step, reported);

            return null;
        }

        if (step.State is StepState.Done or StepState.Skipped or StepState.Failed || reported.State == StepState.Pending)
        {
            return lenient
                ? null
                : DeploymentDecision.Conflict(
                    $"Step {step.Index + 1}, {step.Name}, is {Word(step.State)} and cannot become {Word(reported.State)}. {AskAgain}");
        }

        if (step.State == StepState.Pending && reported.State != StepState.Skipped)
        {
            step.StartedUtc = now;
        }

        // Where a node runs can depend on the path, so it runs in the phase the agent is in when it starts it.
        if (reported.State == StepState.Running)
        {
            step.Phase = phase;
        }

        step.State = reported.State;
        step.FinishedUtc = reported.State == StepState.Running ? null : now;
        step.Percent = reported.State == StepState.Done ? 100 : step.Percent;
        step.Error = StoredText.Bound(reported.Error, DeploymentLimits.MaxErrorLength);
        Decided(step, reported);

        return null;
    }

    // The Pause step the run waits at is its current step, running, with the message the agent worked out. A continue
    // someone gave is kept until the visit it continued is over, since the agent honours it with any report's answer.
    private static void Waits(Deployment run, Dictionary<Guid, DeploymentStep> byId, DeploymentStep? current, AgentRunReport report)
    {
        DeploymentStep? pause = report.Activity == RunActivity.Paused && current is { State: StepState.Running } && current.Kind == RunSnapshots.PauseKind
            ? current
            : null;

        run.PauseStepId = pause?.StepId;
        run.PausePass = pause?.Pass;
        run.PauseMessage = pause is null ? null : StoredText.Bound(report.PauseMessage, DeploymentLimits.MaxPauseMessageLength);

        if (run.ContinueStepId is { } continued
            && !(byId.GetValueOrDefault(continued) is { State: StepState.Running } visit && visit.Pass == run.ContinuePass))
        {
            run.ContinueStepId = null;
            run.ContinuePass = null;
            run.ContinuedByName = null;
        }
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

    // What the visit decided so far: a repeat's time through its body, an IF's branch and the tests behind either. The
    // tests come from outside, so they are held to the bounds the engine keeps.
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

    private static string BodyOf(IfBranch branch) => branch == IfBranch.Then ? StepBody.ThenName : StepBody.ElseName;

    // Without a NUL, which PostgreSQL text cannot hold, and cut rather than refused, like an error.
    private static string Cut(string text, int maxLength)
    {
        string kept = text.Replace("\0", string.Empty, StringComparison.Ordinal);

        return kept.Length <= maxLength ? kept : kept[..maxLength];
    }
}
