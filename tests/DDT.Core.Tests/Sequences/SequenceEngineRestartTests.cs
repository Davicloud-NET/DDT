// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Core.Tests.Sequences;

// A step that restarts the machine and goes on runs again after the restart, with what it kept.
public sealed class SequenceEngineRestartTests
{
    private static readonly MachineVariables s_machine = new(
        "Dell Inc.",
        "Latitude 5440",
        "ABC1234",
        "4c4c4544-0042-3510-8052-b4c04f4d3232",
        ["00155D010203"],
        "PC-0042",
        SequencePhase.Windows);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RunsTheStepAgainAfterTheRestartItAskedForWithWhatItKept()
    {
        ContinuingStep install = Install() with { When = new TestCondition(MachineVariableNames.Model, ConditionOperator.Contains, "Latitude") };
        RunScriptStep next = Script();
        ScriptedStepRunner runner = new ScriptedStepRunner().On(install, (context, _) => Task.FromResult(context.Checkpoint.Saved switch
        {
            null => StepResult.RestartAndContinue("7zip"),
            "7zip" => StepResult.RestartAndContinue("7zip,office"),
            _ => StepResult.Done(),
        }));
        MemoryStateStore store = new();

        SequenceRunResult first = await Engine(runner, store).RunAsync(Start(install, next), s_machine, Token);

        Assert.Equal(SequenceOutcome.RebootRequired, first.Outcome);
        Assert.Equal("Running 0", Line(store.States[^1], install));
        Assert.Equal(new StepTurn(install.Id, 1, true, "7zip"), Assert.Single(store.States[^1].Turns ?? []));

        // The conditions decided once. After the restart they no longer hold, and the step still goes on.
        MachineVariables precision = s_machine with { Model = "Precision 3680" };
        SequenceRunResult second = await Engine(runner, store).RunAsync(store.States[^1], precision, Token);
        SequenceRunResult third = await Engine(runner, store).RunAsync(store.States[^1], precision, Token);

        Assert.Equal(SequenceOutcome.RebootRequired, second.Outcome);
        Assert.Equal(SequenceOutcome.Completed, third.Outcome);
        Assert.Equal([install.Id, install.Id, install.Id, next.Id], runner.Ran);
        Assert.Equal([null, "7zip", "7zip,office"], runner.Runs.Take(3).Select(run => run.Context.Checkpoint.Saved));
        Assert.Equal([0, 1, 2], runner.Runs.Take(3).Select(run => run.Context.Checkpoint.Restarts));
        Assert.All(runner.Runs, run => Assert.False(run.Context.Checkpoint.Interrupted));
        Assert.Equal("Done 2", Line(third.State, install));
        Assert.Null(third.State.Turns);
    }

    [Fact]
    public async Task KeepsTheOutputsGivenWithTheRestart()
    {
        ContinuingStep install = Install();
        RunScriptStep next = Script();
        ScriptedStepRunner runner = new ScriptedStepRunner().On(install, (context, _) => Task.FromResult(context.Checkpoint.Saved is null
            ? StepResult.RestartAndContinue("7zip", Outputs(("ddt.installed", "7zip")))
            : StepResult.Done()));

        SequenceRunResult result = await DriveAsync(Start(install, next), runner, new MemoryStateStore());

        Assert.Equal(SequenceOutcome.Completed, result.Outcome);
        Assert.Equal("7zip", runner.Runs[1].Context.Variables["ddt.installed"]);
        Assert.Equal("7zip", runner.ContextOf(next).Variables["ddt.installed"]);
        Assert.Equal("7zip", result.State.Variables["ddt.installed"]);
    }

    // A runner marks an installer started before it starts it. A power loss then leaves that mark behind.
    [Fact]
    public async Task KeepsACheckpointSavedWhileTheStepRanThroughAPowerLoss()
    {
        ContinuingStep install = Install() with { TakesUp = true };
        MemoryStateStore store = new();
        SequenceState? atPowerLoss = null;
        string? seen = null;
        ScriptedStepRunner runner = new ScriptedStepRunner().On(install, async (context, _) =>
        {
            if (context.Checkpoint.Saved is null)
            {
                await context.Checkpoint.SaveAsync("7zip started");
                seen = context.Checkpoint.Saved;
                atPowerLoss = store.States[^1];
            }

            return StepResult.Done();
        });

        await Engine(runner, store).RunAsync(Start(install), s_machine, Token);

        SequenceState saved = atPowerLoss ?? throw new InvalidOperationException("The checkpoint was not saved.");
        Assert.Equal("7zip started", seen);
        Assert.Equal("Running 0", Line(saved, install));
        Assert.Equal(new StepTurn(install.Id, 1, false, "7zip started"), Assert.Single(saved.Turns ?? []));

        SequenceRunResult resumed = await Engine(runner, new MemoryStateStore()).RunAsync(saved, s_machine, Token);

        Assert.Equal(SequenceOutcome.Completed, resumed.Outcome);
        StepCheckpoint checkpoint = runner.Runs[1].Context.Checkpoint;
        Assert.Equal("7zip started", checkpoint.Saved);
        Assert.True(checkpoint.Interrupted);
        Assert.Equal(1, checkpoint.Restarts);
        Assert.Null(resumed.State.Turns);
    }

    [Fact]
    public async Task FailsTheStepWhenItsCheckpointCannotBeSaved()
    {
        RunScriptStep script = Script();
        ScriptedStepRunner runner = new ScriptedStepRunner().On(script, async (context, _) =>
        {
            await context.Checkpoint.SaveAsync("started");

            return StepResult.Done();
        });
        BlobStore store = new() { FailWhen = state => state.Turns is not null };

        SequenceRunResult result = await Engine(runner, store).RunAsync(Start(script), s_machine, Token);

        Assert.Equal(SequenceOutcome.Failed, result.Outcome);
        Assert.Equal("The run's state could not be saved: There is not enough space on the disk.", result.Error);
        Assert.Equal(StepState.Failed, BlobStore.Load(store.Latest).Steps[0].State);
        Assert.Null(BlobStore.Load(store.Latest).Turns);
    }

    // The entry of a repeat's first time through says nothing about its second.
    [Fact]
    public async Task IgnoresATurnFromAnEarlierPassOfARepeat()
    {
        ContinuingStep install = Install();
        RepeatStep twice = new()
        {
            Id = Guid.NewGuid(),
            Name = "Twice",
            Until = new TestCondition(MachineVariableNames.Model, ConditionOperator.Contains, "Precision"),
            MaxTimes = 2,
            GoOnAtLimit = true,
            Steps = [install],
        };
        MemoryStateStore store = new();
        SequenceState? second = null;
        int runs = 0;
        ScriptedStepRunner runner = new ScriptedStepRunner().On(install, (_, _) =>
        {
            if (++runs == 2)
            {
                second = store.States[^1];
            }

            return Task.FromResult(StepResult.Done());
        });

        await Engine(runner, store).RunAsync(Start(twice), s_machine, Token);

        SequenceState mark = second ?? throw new InvalidOperationException("The step did not run twice.");
        Assert.Equal(2, mark.Steps.Single(step => step.StepId == install.Id).Pass);
        SequenceState stale = mark with { Turns = [new StepTurn(install.Id, 1, true, "the first time")] };
        ScriptedStepRunner after = new();

        SequenceRunResult failed = await Engine(after, new MemoryStateStore()).RunAsync(stale, s_machine, Token);

        Assert.Equal(SequenceOutcome.Failed, failed.Outcome);
        Assert.Equal(SequenceEngine.InterruptedError, failed.Error);
        Assert.Empty(after.Ran);
        Assert.Null(failed.State.Turns);

        SequenceDefinition takesUp = new(SequenceDefinition.CurrentVersion, [twice with { Steps = [install with { TakesUp = true }] }]);
        SequenceRunResult resumed = await Engine(after, new MemoryStateStore()).RunAsync(stale with { Definition = takesUp }, s_machine, Token);

        Assert.Equal(SequenceOutcome.Completed, resumed.Outcome);
        Assert.Null(after.ContextOf(install).Checkpoint.Saved);
        Assert.True(after.ContextOf(install).Checkpoint.Interrupted);
    }

    [Fact]
    public async Task FailsTheStepPastItsLimitWhereContinueOnErrorAppliesAsAlways()
    {
        ContinuingStep install = Install() with { Limit = 1, ContinueOnError = true };
        RunScriptStep next = Script();
        ScriptedStepRunner runner = new ScriptedStepRunner().On(install, StepResult.RestartAndContinue("again"));

        SequenceRunResult result = await DriveAsync(Start(install, next), runner, new MemoryStateStore());

        Assert.Equal(SequenceOutcome.Completed, result.Outcome);
        Assert.Equal([install.Id, install.Id, next.Id], runner.Ran);
        StepRunState failed = result.State.Steps[0];
        Assert.Equal(StepState.Failed, failed.State);
        Assert.Equal(SequenceEngine.TooManyRestarts(1), failed.Error);
        Assert.Equal(2, failed.Restarts);
        Assert.Equal(SequenceEngine.StepFailed, runner.ContextOf(next).Variables[MachineVariableNames.LastStepFailed]);
        Assert.Null(result.State.Turns);

        ScriptedStepRunner strict = new ScriptedStepRunner().On(install, StepResult.RestartAndContinue("again"));
        SequenceRunResult stopped = await DriveAsync(Start(install with { ContinueOnError = false }, next), strict, new MemoryStateStore());

        Assert.Equal(SequenceOutcome.Failed, stopped.Outcome);
        Assert.Equal(SequenceEngine.TooManyRestarts(1), stopped.Error);
        Assert.Equal([install.Id, install.Id], strict.Ran);
    }

    [Fact]
    public async Task TellsAResumableStepThatItWasCutOff()
    {
        ContinuingStep install = Install() with { TakesUp = true };
        SequenceState atPowerLoss = await StateWhileRunningAsync(install, Start(install));
        ScriptedStepRunner after = new();
        MemoryStateStore store = new();

        SequenceRunResult resumed = await Engine(after, store).RunAsync(atPowerLoss, s_machine, Token);

        Assert.Equal(SequenceOutcome.Completed, resumed.Outcome);
        StepCheckpoint checkpoint = after.ContextOf(install).Checkpoint;
        Assert.True(checkpoint.Interrupted);
        Assert.Equal(1, checkpoint.Restarts);
        Assert.Null(checkpoint.Saved);
        Assert.Equal(["Running 1", "Done 1"], store.States.Select(state => Line(state, install)));
    }

    [Fact]
    public async Task FailsAKindWithoutALimitThatAsksToGoOn()
    {
        RunScriptStep script = Script() with { ContinueOnError = true };
        RunScriptStep next = Script();
        ScriptedStepRunner runner = new ScriptedStepRunner().On(script, StepResult.RestartAndContinue("half", Outputs(("ddt.half", "1"))));
        BlobStore store = new();

        SequenceRunResult result = await Engine(runner, store).RunAsync(Start(script, next), s_machine, Token);

        Assert.Equal(SequenceOutcome.Completed, result.Outcome);
        Assert.Equal([script.Id, next.Id], runner.Ran);
        Assert.Equal(new StepRunState(script.Id, StepState.Failed, SequenceEngine.RestartNotAllowedError), result.State.Steps[0]);
        Assert.False(result.State.Variables.ContainsKey("ddt.half"));
        Assert.All(store.Blobs, blob => Assert.DoesNotContain("\"turns\"", blob, StringComparison.Ordinal));
    }

    // Pause runs again when found Running, as it always did. Nothing new appears in its state or in what is reported.
    [Fact]
    public async Task LeavesTheStateOfTheKindsThatCameBeforeAsItWas()
    {
        PauseStep pause = new() { Id = Guid.NewGuid(), Name = "Pause", Message = "Check the cables." };
        RunScriptStep script = Script();
        BlobStore before = new();
        string? atPowerLoss = null;
        ScriptedStepRunner first = new ScriptedStepRunner().On(pause, (_, _) =>
        {
            atPowerLoss ??= before.Latest;

            return Task.FromResult(StepResult.Done());
        });

        await Engine(first, before).RunAsync(Start(pause, script), s_machine, Token);

        BlobStore after = new();
        ScriptedStepRunner again = new();
        SequenceRunResult resumed = await Engine(again, after)
            .RunAsync(BlobStore.Load(atPowerLoss ?? throw new InvalidOperationException("The pause did not run.")), s_machine, Token);

        Assert.Equal(SequenceOutcome.Completed, resumed.Outcome);
        Assert.Equal([pause.Id, script.Id], again.Ran);
        Assert.Equal(StepState.Done, BlobStore.Load(after.Blobs[0]).Steps[0].State);
        Assert.All(resumed.State.Steps, step => Assert.Equal(0, step.Restarts));
        Assert.All(
            before.Blobs.Concat(after.Blobs),
            blob =>
            {
                Assert.DoesNotContain("\"restarts\"", blob, StringComparison.Ordinal);
                Assert.DoesNotContain("\"turns\"", blob, StringComparison.Ordinal);
            });
    }

    // Runs the sequence until the step starts and returns the state saved at that moment, as a power loss would leave
    // it.
    private static async Task<SequenceState> StateWhileRunningAsync(SequenceStep step, SequenceState start)
    {
        MemoryStateStore store = new();
        SequenceState? mark = null;
        ScriptedStepRunner runner = new ScriptedStepRunner().On(step, (_, _) =>
        {
            mark ??= store.States[^1];

            return Task.FromResult(StepResult.Done());
        });

        await Engine(runner, store).RunAsync(start, s_machine, Token);

        return mark ?? throw new InvalidOperationException("The step did not run.");
    }

    // Runs the way the agent does. After each restart it resumes from the latest saved state in a fresh engine.
    private static async Task<SequenceRunResult> DriveAsync(SequenceState state, ScriptedStepRunner runner, MemoryStateStore store)
    {
        for (int start = 0; start < 10; start++)
        {
            SequenceRunResult result = await Engine(runner, store).RunAsync(state, s_machine, Token);

            if (result.Outcome != SequenceOutcome.RebootRequired)
            {
                return result;
            }

            state = store.States[^1];
        }

        throw new InvalidOperationException("The run did not end.");
    }

    private static SequenceEngine Engine(ScriptedStepRunner runner, ISequenceStateStore store) => new(runner, store, new StepPercents());

    private static SequenceState Start(params SequenceStep[] steps) =>
        SequenceStates.Start(Guid.NewGuid(), new SequenceDefinition(SequenceDefinition.CurrentVersion, steps)) with
        {
            Phase = SequencePhase.Windows,
        };

    private static string Line(SequenceState state, SequenceStep step)
    {
        StepRunState run = state.Steps.Single(candidate => candidate.StepId == step.Id);

        return $"{run.State} {run.Restarts}";
    }

    private static Dictionary<string, string> Outputs(params (string Name, string Value)[] outputs) =>
        outputs.ToDictionary(output => output.Name, output => output.Value, StringComparer.Ordinal);

    private static ContinuingStep Install() => new() { Id = Guid.NewGuid(), Name = "Install applications" };

    private static RunScriptStep Script() =>
        new() { Id = Guid.NewGuid(), Name = "Run a script", Phase = SequencePhase.Windows, Script = "exit /b 0" };
}
