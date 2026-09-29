// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Templates;

namespace DDT.Core.Sequences;

// Runs a run's tree from its cursor on, in the run's current phase. A leaf's Running mark is saved before it runs. So a
// leaf still marked Running after a restart has failed and doesn't run again, unless it's Resumable. Containers have
// no side effects, so entering and leaving them waits for the next save. A resume repeats those moves the same way.
public sealed class SequenceEngine(IStepRunner runner, ISequenceStateStore store, IProgress<StepPercent> progress)
{
    public const string InterruptedError = "The machine restarted or the agent stopped while this step ran.";

    // What LastStepFailed holds after a step has run.
    public const string StepFailed = "Yes";
    public const string StepSucceeded = "No";

    public async Task<SequenceRunResult> RunAsync(SequenceState state, MachineVariables machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(machine);

        SequenceWalk walk = new(state);

        while (walk.Cursor is { } cursor)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new SequenceRunResult(SequenceOutcome.Stopped, walk.Saved, null);
            }

            SequenceStep node = walk.Node(cursor.NodeId);

            if (cursor.Leaving)
            {
                if (walk.Leave(node, walk.Machine(machine, walk.Phase)) is { } error)
                {
                    return await SaveOrFailAsync(walk).ConfigureAwait(false) ?? walk.Failed(error);
                }
            }
            else if (node.IsContainer)
            {
                walk.Enter(node, walk.Machine(machine, walk.Phase));
            }
            else if (await RunLeafAsync(walk, node, machine, cancellationToken).ConfigureAwait(false) is { } ended)
            {
                return ended;
            }
        }

        // Leaving the last containers hasn't been saved yet.
        if (walk.Changed && await SaveOrFailAsync(walk).ConfigureAwait(false) is { } failed)
        {
            return failed;
        }

        return walk.Result(SequenceOutcome.Completed);
    }

    // Null when the run continues after the leaf.
    private async Task<SequenceRunResult?> RunLeafAsync(
        SequenceWalk walk,
        SequenceStep step,
        MachineVariables machine,
        CancellationToken cancellationToken)
    {
        // A step that doesn't ask for a phase, such as a restart, runs in the current phase. A step for the other
        // phase ends this part of the run. The host hands the run over and calls the engine again in that phase, at
        // this step.
        SequencePhase phase = step.RequiredPhase ?? walk.Phase;

        if (phase != walk.Phase)
        {
            return await SaveOrFailAsync(walk).ConfigureAwait(false) ?? walk.Result(SequenceOutcome.PhaseChangeRequired);
        }

        MachineVariables inPhase = walk.Machine(machine, phase);
        int order = walk.OrderOf(step);
        bool found = walk[order].State == StepState.Running;

        if (found && !step.Resumable)
        {
            return await RecordAsync(walk, step, order, StepResult.Failed(InterruptedError)).ConfigureAwait(false);
        }

        // A resumable step still marked Running continues the visit its mark was saved for. Its conditions aren't
        // checked again.
        if (!found)
        {
            ConditionResult conditions = ConditionEvaluator.Evaluate(step, inPhase);

            if (!conditions.Held)
            {
                walk.Skip(order, conditions.Evaluations);

                return await SaveOrFailAsync(walk).ConfigureAwait(false);
            }

            if (await BeginAsync(walk, order, conditions.Evaluations).ConfigureAwait(false) is { } failed)
            {
                return failed;
            }
        }

        StepContext context = new(walk.RunId, phase, inPhase, walk.Variables, new StepPercentProgress(step.Id, progress));
        StepResult result = step is SetVariableStep set
            ? SetVariable(set, walk.Definition, inPhase)
            : await RunStepAsync(step, context, cancellationToken).ConfigureAwait(false);

        // A step that fails after a stop is requested counts as stopped, however it failed. Its Running mark stays
        // saved. A resumed run then fails it as interrupted instead of moving past it, or runs it again if it's
        // resumable.
        if (result.Outcome == StepOutcome.Failed && cancellationToken.IsCancellationRequested)
        {
            return new SequenceRunResult(SequenceOutcome.Stopped, walk.Saved, null);
        }

        return await RecordAsync(walk, step, order, result).ConfigureAwait(false);
    }

    // Saves the Running mark before the leaf runs. If the save fails, the leaf and the run fail.
    private async Task<SequenceRunResult?> BeginAsync(SequenceWalk walk, int order, IReadOnlyList<TestEvaluation> evaluations)
    {
        walk.Begin(order, evaluations);

        if (await SaveAsync(walk).ConfigureAwait(false) is not { } error)
        {
            return null;
        }

        walk.Mark(order, StepState.Failed, error);

        return walk.Failed(error);
    }

    // Null when the run continues after the leaf.
    private async Task<SequenceRunResult?> RecordAsync(SequenceWalk walk, SequenceStep step, int order, StepResult result)
    {
        walk.Record(result);

        if (result.Outcome == StepOutcome.Failed)
        {
            string error = result.Error ?? "The step failed.";
            bool caught = walk.Fail(order, error);

            return await SaveOrFailAsync(walk).ConfigureAwait(false) ?? (caught ? null : walk.Failed(error));
        }

        walk.Finish(order);

        if (await SaveOrFailAsync(walk).ConfigureAwait(false) is { } failed)
        {
            return failed;
        }

        // The saved state points at the step after this one, so a restart continues there, even inside a repeat or
        // an IF.
        return result.Outcome == StepOutcome.RebootRequired || step.RebootAfter ? walk.Result(SequenceOutcome.RebootRequired) : null;
    }

    private async Task<StepResult> RunStepAsync(SequenceStep step, StepContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await runner.RunAsync(step, context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return StepResult.Failed(exception.Message);
        }
    }

    // Needs nothing from the agent. It renders a template over the run's values, the variables steps set and the
    // machine's facts. The result goes into a variable the sequence lets steps set, under the declared name.
    private static StepResult SetVariable(SetVariableStep step, SequenceDefinition definition, MachineVariables machine)
    {
        VariableDeclaration? declared = definition.Variables?.FirstOrDefault(variable =>
            variable is not null && string.Equals(variable.Name, step.Variable, StringComparison.OrdinalIgnoreCase));

        if (declared is not { SetBySteps: true })
        {
            return StepResult.Failed(
                $"{step.Variable} cannot be set: the sequence does not declare it as a variable that steps may set.");
        }

        if (!ValueTemplate.TryRender(step.Value ?? "", machine.Value, out string value, out TemplateProblem? problem))
        {
            return StepResult.Failed($"{declared.Name} cannot be set: {problem?.Message().Text}");
        }

        return StepResult.Done(new Dictionary<string, string>(StringComparer.Ordinal) { [declared.Name] = value });
    }

    // If the save fails, the run fails with the save's error.
    private async Task<SequenceRunResult?> SaveOrFailAsync(SequenceWalk walk) =>
        await SaveAsync(walk).ConfigureAwait(false) is { } error ? walk.Failed(error) : null;

    // Saves never take the stop token. A stop mustn't lose a finished step, and a step mustn't start before its Running
    // mark is saved.
    private async Task<string?> SaveAsync(SequenceWalk walk)
    {
        SequenceState state = walk.Snapshot();

        try
        {
            await store.SaveAsync(state, CancellationToken.None).ConfigureAwait(false);
            walk.WasSaved(state);

            return null;
        }
        catch (Exception exception)
        {
            return $"The run's state could not be saved: {exception.Message}";
        }
    }
}
