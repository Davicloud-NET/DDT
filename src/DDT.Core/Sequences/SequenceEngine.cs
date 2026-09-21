// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// Runs a run's steps from state.NextIndex on, within the phase the run is in, and saves the state before and after
// every step. The Running mark saved before a step makes an interruption visible: a step found Running when the
// engine starts failed, and is never run again, because steps such as Partition or a script are not repeatable.
public sealed class SequenceEngine(IStepRunner runner, ISequenceStateStore store, IProgress<StepPercent> progress)
{
    public const string InterruptedError = "The machine restarted or the agent stopped while this step ran.";

    public async Task<SequenceRunResult> RunAsync(SequenceState state, MachineVariables machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(machine);

        IReadOnlyList<SequenceStep> steps = state.Definition.Steps;

        if (state.Steps.Count != steps.Count)
        {
            throw new ArgumentException("The run's state does not have one entry per step of its definition.", nameof(state));
        }

        for (int index = state.NextIndex; index < steps.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new SequenceRunResult(SequenceOutcome.Stopped, state, null);
            }

            SequenceStep step = steps[index];
            SequencePhase phase = SequencePhases.Of(state.Definition, index);
            MachineVariables inPhase = machine with { Phase = phase };

            if (phase != state.Phase)
            {
                state = state with { NextIndex = index };

                return await SaveAsync(state).ConfigureAwait(false) is { } phaseError
                    ? Failed(state, phaseError)
                    : new SequenceRunResult(SequenceOutcome.PhaseChangeRequired, state, null);
            }

            StepResult result;

            if (state.Steps[index].State == StepState.Running)
            {
                result = StepResult.Failed(InterruptedError);
            }
            else if (!ConditionEvaluator.Holds(step.Conditions, inPhase))
            {
                state = Mark(state, index, StepState.Skipped, null) with { NextIndex = index + 1 };

                if (await SaveAsync(state).ConfigureAwait(false) is { } skipError)
                {
                    return Failed(state, skipError);
                }

                continue;
            }
            else
            {
                state = Mark(state, index, StepState.Running, null);

                if (await SaveAsync(state).ConfigureAwait(false) is { } markError)
                {
                    state = Mark(state, index, StepState.Failed, markError);

                    return Failed(state, markError);
                }

                StepContext context = new(state.RunId, phase, inPhase, state.Variables, new StepPercentProgress(step.Id, progress));

                try
                {
                    result = await runner.RunAsync(step, context, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    result = StepResult.Failed(exception.Message);
                }

                // A step that fails once a stop is requested counts as stopped, whether its runner threw
                // OperationCanceledException or returned Failed. The Running mark stays saved, so a resumed run fails
                // the step as interrupted instead of going on past it.
                if (result.Outcome == StepOutcome.Failed && cancellationToken.IsCancellationRequested)
                {
                    return new SequenceRunResult(SequenceOutcome.Stopped, state, null);
                }
            }

            IReadOnlyDictionary<string, string> variables = Merge(state.Variables, result.Outputs);

            if (result.Outcome == StepOutcome.Failed)
            {
                string error = result.Error ?? "The step failed.";
                state = Mark(state, index, StepState.Failed, error) with { NextIndex = index + 1, Variables = variables };

                if (await SaveAsync(state).ConfigureAwait(false) is { } failedError)
                {
                    return Failed(state, failedError);
                }

                if (!step.ContinueOnError)
                {
                    return Failed(state, error);
                }

                continue;
            }

            state = Mark(state, index, StepState.Done, null) with { NextIndex = index + 1, Variables = variables };

            if (await SaveAsync(state).ConfigureAwait(false) is { } doneError)
            {
                return Failed(state, doneError);
            }

            if (result.Outcome == StepOutcome.RebootRequired || step.RebootAfter)
            {
                return new SequenceRunResult(SequenceOutcome.RebootRequired, state, null);
            }
        }

        return new SequenceRunResult(SequenceOutcome.Completed, state, null);
    }

    // Saves never take the stop token: a stop must not lose a finished step, and a step must not start before its
    // Running mark is saved.
    private async Task<string?> SaveAsync(SequenceState state)
    {
        try
        {
            await store.SaveAsync(state, CancellationToken.None).ConfigureAwait(false);

            return null;
        }
        catch (Exception exception)
        {
            return $"The run's state could not be saved: {exception.Message}";
        }
    }

    private static SequenceRunResult Failed(SequenceState state, string error) => new(SequenceOutcome.Failed, state, error);

    private static SequenceState Mark(SequenceState state, int index, StepState stepState, string? error)
    {
        StepRunState[] steps = [.. state.Steps];
        steps[index] = steps[index] with { State = stepState, Error = error };

        return state with { Steps = steps };
    }

    private static IReadOnlyDictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> variables,
        IReadOnlyDictionary<string, string>? outputs)
    {
        if (outputs is null || outputs.Count == 0)
        {
            return variables;
        }

        Dictionary<string, string> merged = new(variables, StringComparer.Ordinal);

        foreach ((string name, string value) in outputs)
        {
            merged[name] = value;
        }

        return merged;
    }
}
