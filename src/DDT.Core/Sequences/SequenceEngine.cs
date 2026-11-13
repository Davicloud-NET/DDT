// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Templates;

namespace DDT.Core.Sequences;

// Runs a run's tree from its cursor on, in the run's current phase. A leaf's Running mark is saved before it runs. So a
// leaf still marked Running after a restart has failed and doesn't run again, unless it's Resumable or asked for that
// restart. Containers have no side effects, so entering and leaving them waits for the next save. A resume repeats those
// moves the same way.
public sealed class SequenceEngine(IStepRunner runner, ISequenceStateStore store, IProgress<StepPercent> progress)
{
    public const string InterruptedError = "The machine restarted or the agent stopped while this step ran.";

    public const string RestartNotAllowedError = "The step asked to restart the machine and go on, which this kind of step may not do.";

    // What LastStepFailed holds after a step has run.
    public const string StepFailed = "Yes";
    public const string StepSucceeded = "No";

    public static string TooManyRestarts(int limit) =>
        $"The machine restarted more than {limit} times while this step ran, more than the step may have.";

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
        StepTurn? turn = found ? walk.Turns.Of(step.Id, walk[order].Pass) : null;
        (bool runs, SequenceRunResult? ended) = found
            ? await TakeUpAsync(walk, step, order, turn).ConfigureAwait(false)
            : await StartAsync(walk, step, order, inPhase).ConfigureAwait(false);

        if (!runs)
        {
            return ended;
        }

        StepContext context = new(walk.RunId, phase, inPhase, walk.Variables, new StepPercentProgress(step.Id, progress))
        {
            Checkpoint = Checkpoint(walk, step, order, turn?.Progress, found && turn is not { RestartDue: true }),
        };
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

    // The leaf runs once its conditions hold and its Running mark is saved.
    private async Task<(bool Runs, SequenceRunResult? Ended)> StartAsync(
        SequenceWalk walk,
        SequenceStep step,
        int order,
        MachineVariables machine)
    {
        ConditionResult conditions = ConditionEvaluator.Evaluate(step, machine);

        if (!conditions.Held)
        {
            walk.Skip(order, conditions.Evaluations);

            return (false, await SaveOrFailAsync(walk).ConfigureAwait(false));
        }

        walk.Begin(order, conditions.Evaluations);

        return await SaveMarkAsync(walk, order).ConfigureAwait(false) is { } failed ? (false, failed) : (true, null);
    }

    // A leaf still marked Running continues the visit its mark was saved for. Its conditions aren't checked again.
    private async Task<(bool Runs, SequenceRunResult? Ended)> TakeUpAsync(SequenceWalk walk, SequenceStep step, int order, StepTurn? turn)
    {
        bool due = turn is { RestartDue: true };

        if (!due && !step.Resumable)
        {
            return (false, await RecordAsync(walk, step, order, StepResult.Failed(InterruptedError)).ConfigureAwait(false));
        }

        // A kind without a limit runs again as it always did, so its saved state doesn't change.
        if (!due && step.RestartLimit == 0)
        {
            return (true, null);
        }

        walk.Restarted(order);

        if (turn is not null)
        {
            walk.Turns.Keep(turn with { RestartDue = false });
        }

        if (step.RestartLimit > 0 && walk[order].Restarts > step.RestartLimit)
        {
            StepResult over = StepResult.Failed(TooManyRestarts(step.RestartLimit));

            return (false, await RecordAsync(walk, step, order, over).ConfigureAwait(false));
        }

        return await SaveMarkAsync(walk, order).ConfigureAwait(false) is { } unsaved ? (false, unsaved) : (true, null);
    }

    private StepCheckpoint Checkpoint(SequenceWalk walk, SequenceStep step, int order, string? saved, bool interrupted)
    {
        int pass = walk[order].Pass;

        return new StepCheckpoint(saved, walk[order].Restarts, interrupted, async kept =>
        {
            walk.Turns.Keep(new StepTurn(step.Id, pass, false, kept));

            if (await SaveAsync(walk).ConfigureAwait(false) is { } error)
            {
                throw new IOException(error);
            }
        });
    }

    // Saves the Running mark before the leaf runs. If the save fails, the leaf and the run fail.
    private async Task<SequenceRunResult?> SaveMarkAsync(SequenceWalk walk, int order)
    {
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
        if (result.Outcome == StepOutcome.RestartAndContinue)
        {
            if (step.RestartLimit > 0)
            {
                return await RestartAndContinueAsync(walk, step, order, result).ConfigureAwait(false);
            }

            result = StepResult.Failed(RestartNotAllowedError);
        }

        walk.Turns.Drop(step.Id);
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

    // The step stays Running and isn't finished. Its turn says the restart is due, so it runs again after it.
    private async Task<SequenceRunResult> RestartAndContinueAsync(SequenceWalk walk, SequenceStep step, int order, StepResult result)
    {
        walk.Record(result);
        walk.Turns.Keep(new StepTurn(step.Id, walk[order].Pass, true, result.Progress));

        return await SaveOrFailAsync(walk).ConfigureAwait(false) ?? walk.Result(SequenceOutcome.RebootRequired);
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
