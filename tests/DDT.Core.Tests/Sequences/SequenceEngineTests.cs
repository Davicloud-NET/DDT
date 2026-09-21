// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Core.Tests.Sequences;

public sealed class SequenceEngineTests
{
    private const string ExitCodeOne = "The script ended with exit code 1.";

    private static readonly MachineVariables s_machine = new(
        "Dell Inc.",
        "Latitude 5440",
        "ABC1234",
        "4c4c4544-0042-3510-8052-b4c04f4d3232",
        ["00155D010203"],
        "PC-0042",
        SequencePhase.WindowsPE);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void StartsBeforeTheFirstStepInWindowsPE()
    {
        PartitionStep partition = Partition();
        Guid runId = Guid.NewGuid();

        SequenceState state = SequenceStates.Start(runId, Definition(partition, Reboot()));

        Assert.Equal(SequenceState.CurrentFormat, state.Format);
        Assert.Equal(runId, state.RunId);
        Assert.Equal(SequencePhase.WindowsPE, state.Phase);
        Assert.Equal(0, state.NextIndex);
        Assert.Equal(new StepRunState(partition.Id, StepState.Pending, null), state.Steps[0]);
        Assert.All(state.Steps, step => Assert.Equal(StepState.Pending, step.State));
        Assert.Empty(state.Variables);
    }

    [Fact]
    public async Task RunsTheStepsInOrderAndSavesBeforeAndAfterEach()
    {
        PartitionStep partition = Partition();
        ApplyImageStep apply = ApplyImage();
        RunScriptStep script = Script(SequencePhase.WindowsPE);
        ScriptedStepRunner runner = new();
        BlobStore store = new();

        SequenceRunResult result = await Engine(runner, store).RunAsync(Start(partition, apply, script), s_machine, Token);

        Assert.Equal(SequenceOutcome.Completed, result.Outcome);
        Assert.Null(result.Error);
        Assert.Equal([partition.Id, apply.Id, script.Id], runner.Ran);
        Assert.Equal(
            [
                "0 Running Pending Pending",
                "1 Done Pending Pending",
                "1 Done Running Pending",
                "2 Done Done Pending",
                "2 Done Done Running",
                "3 Done Done Done",
            ],
            store.States.Select(Summary));
        Assert.Equal(SequencePhase.WindowsPE, result.State.Phase);
    }

    // The brief's rule for the engine: serialise the state mid-sequence and resume from the blob in a fresh engine.
    [Fact]
    public async Task ResumesFromTheSavedBlobInAFreshEngineAfterARestart()
    {
        PartitionStep partition = Partition();
        RunScriptStep restarting = Script(SequencePhase.WindowsPE) with { RebootAfter = true };
        RunScriptStep second = Script(SequencePhase.WindowsPE);
        RunScriptStep third = Script(SequencePhase.WindowsPE);
        SequenceState start = Start(partition, restarting, second, third);
        ScriptedStepRunner before = new ScriptedStepRunner().On(partition, StepResult.Done(Outputs(("ddt.disk.windows", "7c1f"))));
        BlobStore store = new();

        SequenceRunResult restart = await Engine(before, store).RunAsync(start, s_machine, Token);

        Assert.Equal(SequenceOutcome.RebootRequired, restart.Outcome);
        Assert.Equal([partition.Id, restarting.Id], before.Ran);

        ScriptedStepRunner after = new();
        BlobStore afterStore = new();

        SequenceRunResult resumed = await Engine(after, afterStore).RunAsync(BlobStore.Load(store.Latest), s_machine, Token);

        Assert.Equal(SequenceOutcome.Completed, resumed.Outcome);
        Assert.Equal([second.Id, third.Id], after.Ran);
        Assert.Equal(start.RunId, after.ContextOf(second).RunId);
        Assert.Equal("7c1f", after.ContextOf(second).Variables["ddt.disk.windows"]);
        Assert.Equal(4, resumed.State.NextIndex);
        Assert.All(resumed.State.Steps, step => Assert.Equal(StepState.Done, step.State));
        Assert.Equal("4 Done Done Done Done", Summary(BlobStore.Load(afterStore.Latest)));
    }

    [Fact]
    public async Task FailsAStepThatWasRunningWhenTheMachineRestarted()
    {
        PartitionStep partition = Partition();
        RunScriptStep interrupted = Script(SequencePhase.WindowsPE);
        RunScriptStep next = Script(SequencePhase.WindowsPE);
        string atPowerLoss = await BlobWhileRunningAsync(interrupted, Start(partition, interrupted, next));
        ScriptedStepRunner after = new();

        SequenceRunResult resumed = await Engine(after).RunAsync(BlobStore.Load(atPowerLoss), s_machine, Token);

        Assert.Equal(SequenceOutcome.Failed, resumed.Outcome);
        Assert.Equal(SequenceEngine.InterruptedError, resumed.Error);
        Assert.Empty(after.Ran);
        Assert.Equal(new StepRunState(interrupted.Id, StepState.Failed, SequenceEngine.InterruptedError), resumed.State.Steps[1]);
        Assert.Equal("2 Done Failed Pending", Summary(resumed.State));
    }

    [Fact]
    public async Task ContinuesAfterAnInterruptedStepThatAllowsIt()
    {
        PartitionStep partition = Partition();
        RunScriptStep interrupted = Script(SequencePhase.WindowsPE) with { ContinueOnError = true };
        RunScriptStep next = Script(SequencePhase.WindowsPE);
        string atPowerLoss = await BlobWhileRunningAsync(interrupted, Start(partition, interrupted, next));
        ScriptedStepRunner after = new();

        SequenceRunResult resumed = await Engine(after).RunAsync(BlobStore.Load(atPowerLoss), s_machine, Token);

        Assert.Equal(SequenceOutcome.Completed, resumed.Outcome);
        Assert.Equal([next.Id], after.Ran);
        Assert.Equal("3 Done Failed Done", Summary(resumed.State));
    }

    [Fact]
    public async Task HandsTheRunOverToWindowsAndResumesThere()
    {
        PartitionStep partition = Partition();
        ApplyImageStep apply = ApplyImage();
        RunScriptStep inWindows = Script(SequencePhase.Windows) with
        {
            Conditions = [new StepCondition(MachineVariableNames.Phase, ConditionOperator.Equals, "Windows")],
        };
        JoinDomainStep join = JoinDomain();
        ScriptedStepRunner windowsPE = new();
        BlobStore store = new();

        SequenceRunResult handOver = await Engine(windowsPE, store).RunAsync(Start(partition, apply, inWindows, join), s_machine, Token);

        Assert.Equal(SequenceOutcome.PhaseChangeRequired, handOver.Outcome);
        Assert.Equal([partition.Id, apply.Id], windowsPE.Ran);
        Assert.Equal("2 Done Done Pending Pending", Summary(BlobStore.Load(store.Latest)));

        // The hand-over saves the state in the Windows phase for the service in Windows to resume.
        SequenceState staged = BlobStore.Load(store.Latest) with { Phase = SequencePhase.Windows };
        ScriptedStepRunner windows = new();

        SequenceRunResult finished = await Engine(windows).RunAsync(staged, s_machine, Token);

        Assert.Equal(SequenceOutcome.Completed, finished.Outcome);
        Assert.Equal([inWindows.Id, join.Id], windows.Ran);
        Assert.All(windows.Runs, run => Assert.Equal(SequencePhase.Windows, run.Context.Phase));
        Assert.All(windows.Runs, run => Assert.Equal(SequencePhase.Windows, run.Context.Machine.Phase));
    }

    // M6 adds kinds such as a raw image write: the agent's runner handles them, and the engine stays as it is.
    [Fact]
    public async Task RunsAKindItDoesNotKnowAndCompletesWithoutWindows()
    {
        WriteRawImageStep raw = new() { Id = Guid.NewGuid(), Name = "Write the raw image" };
        RunScriptStep seed = Script(SequencePhase.WindowsPE);
        ScriptedStepRunner runner = new ScriptedStepRunner().On(raw, StepResult.Done(Outputs(("ddt.raw-written", "1"))));
        MemoryStateStore store = new();

        SequenceRunResult result = await new SequenceEngine(runner, store, new StepPercents())
            .RunAsync(Start(raw, seed), s_machine, Token);

        Assert.Equal(SequenceOutcome.Completed, result.Outcome);
        Assert.Equal([raw.Id, seed.Id], runner.Ran);
        Assert.Equal(SequencePhase.WindowsPE, result.State.Phase);
        Assert.Equal("1", result.State.Variables["ddt.raw-written"]);
        Assert.Equal(4, store.States.Count);
    }

    [Fact]
    public async Task SkipsAStepWhoseConditionsDoNotHold()
    {
        PartitionStep partition = Partition();
        RunScriptStep precision = Script(SequencePhase.WindowsPE) with
        {
            Conditions = [new StepCondition(MachineVariableNames.Model, ConditionOperator.StartsWith, "Precision")],
        };
        ScriptedStepRunner runner = new();
        BlobStore store = new();

        SequenceRunResult result = await Engine(runner, store).RunAsync(Start(partition, precision), s_machine, Token);

        Assert.Equal(SequenceOutcome.Completed, result.Outcome);
        Assert.Equal([partition.Id], runner.Ran);
        Assert.Equal(["0 Running Pending", "1 Done Pending", "2 Done Skipped"], store.States.Select(Summary));
    }

    [Fact]
    public async Task ContinuesAfterAFailedStepThatAllowsIt()
    {
        PartitionStep partition = Partition();
        RunScriptStep failing = Script(SequencePhase.WindowsPE) with { ContinueOnError = true };
        RunScriptStep next = Script(SequencePhase.WindowsPE);
        ScriptedStepRunner runner = new ScriptedStepRunner().On(failing, StepResult.Failed(ExitCodeOne));

        SequenceRunResult result = await Engine(runner).RunAsync(Start(partition, failing, next), s_machine, Token);

        Assert.Equal(SequenceOutcome.Completed, result.Outcome);
        Assert.Equal([partition.Id, failing.Id, next.Id], runner.Ran);
        Assert.Equal(new StepRunState(failing.Id, StepState.Failed, ExitCodeOne), result.State.Steps[1]);
        Assert.Equal("3 Done Failed Done", Summary(result.State));
    }

    [Fact]
    public async Task StopsTheRunWhenAStepFails()
    {
        PartitionStep partition = Partition();
        RunScriptStep failing = Script(SequencePhase.WindowsPE);
        RunScriptStep next = Script(SequencePhase.WindowsPE);
        ScriptedStepRunner runner = new ScriptedStepRunner().On(failing, StepResult.Failed(ExitCodeOne));
        BlobStore store = new();

        SequenceRunResult result = await Engine(runner, store).RunAsync(Start(partition, failing, next), s_machine, Token);

        Assert.Equal(SequenceOutcome.Failed, result.Outcome);
        Assert.Equal(ExitCodeOne, result.Error);
        Assert.Equal([partition.Id, failing.Id], runner.Ran);
        Assert.Equal("2 Done Failed Pending", Summary(BlobStore.Load(store.Latest)));
    }

    [Fact]
    public async Task RestartsAfterAStepMarkedRebootAfter()
    {
        PartitionStep partition = Partition() with { RebootAfter = true };
        RunScriptStep next = Script(SequencePhase.WindowsPE);
        ScriptedStepRunner runner = new();

        SequenceRunResult result = await Engine(runner).RunAsync(Start(partition, next), s_machine, Token);

        Assert.Equal(SequenceOutcome.RebootRequired, result.Outcome);
        Assert.Equal([partition.Id], runner.Ran);
        Assert.Equal("1 Done Pending", Summary(result.State));
    }

    [Fact]
    public async Task RestartsWhenAStepAsksForIt()
    {
        PartitionStep partition = Partition();
        RebootStep reboot = Reboot();
        RunScriptStep next = Script(SequencePhase.WindowsPE);
        ScriptedStepRunner runner = new ScriptedStepRunner().On(reboot, StepResult.RebootRequired());

        SequenceRunResult result = await Engine(runner).RunAsync(Start(partition, reboot, next), s_machine, Token);

        Assert.Equal(SequenceOutcome.RebootRequired, result.Outcome);
        Assert.Equal([partition.Id, reboot.Id], runner.Ran);
        Assert.Equal("2 Done Done Pending", Summary(result.State));
    }

    [Fact]
    public async Task MergesStepOutputsIntoTheRunVariables()
    {
        PartitionStep partition = Partition();
        RunScriptStep script = Script(SequencePhase.WindowsPE);
        ScriptedStepRunner runner = new ScriptedStepRunner()
            .On(partition, StepResult.Done(Outputs(("a", "1"), ("b", "1"))))
            .On(script, StepResult.Done(Outputs(("b", "2"), ("c", "3"))));
        BlobStore store = new();

        SequenceRunResult result = await Engine(runner, store).RunAsync(Start(partition, script), s_machine, Token);

        Assert.Equal("1", runner.ContextOf(script).Variables["b"]);
        Assert.Equal(Outputs(("a", "1"), ("b", "2"), ("c", "3")), result.State.Variables);
        Assert.Equal(result.State.Variables, BlobStore.Load(store.Latest).Variables);
    }

    [Fact]
    public async Task ReportsTheStepPercent()
    {
        PartitionStep partition = Partition();
        ScriptedStepRunner runner = new ScriptedStepRunner().On(partition, (context, _) =>
        {
            context.Progress.Report(40);

            return Task.FromResult(StepResult.Done());
        });
        StepPercents percents = new();

        await Engine(runner, percents: percents).RunAsync(Start(partition), s_machine, Token);

        Assert.Equal([new StepPercent(partition.Id, 40)], percents.Reports);
    }

    [Theory]
    [InlineData(StepState.Done)]
    [InlineData(StepState.Skipped)]
    [InlineData(StepState.Failed)]
    public async Task FailsTheRunWhenTheStateCannotBeSaved(StepState ending)
    {
        PartitionStep partition = Partition();
        RunScriptStep step = Script(SequencePhase.WindowsPE) with
        {
            ContinueOnError = true,
            Conditions = ending == StepState.Skipped
                ? [new StepCondition(MachineVariableNames.Model, ConditionOperator.StartsWith, "Precision")]
                : [],
        };
        RunScriptStep next = Script(SequencePhase.WindowsPE);
        ScriptedStepRunner runner = new ScriptedStepRunner()
            .On(step, ending == StepState.Failed ? StepResult.Failed(ExitCodeOne) : StepResult.Done());

        // Only the save that records how the step ended fails, so a run that ignored it would go on to the next step.
        BlobStore store = new() { FailWhen = state => state.Steps[1].State == ending && state.Steps[2].State == StepState.Pending };

        SequenceRunResult result = await Engine(runner, store).RunAsync(Start(partition, step, next), s_machine, Token);

        Assert.Equal(SequenceOutcome.Failed, result.Outcome);
        Assert.Equal("The run's state could not be saved: There is not enough space on the disk.", result.Error);
        Assert.DoesNotContain(next.Id, runner.Ran);
    }

    [Fact]
    public async Task DoesNotStartAStepWhoseRunningMarkCannotBeSaved()
    {
        PartitionStep partition = Partition();
        ScriptedStepRunner runner = new();
        BlobStore store = new() { FailWhen = state => state.Steps[0].State == StepState.Running };

        SequenceRunResult result = await Engine(runner, store).RunAsync(Start(partition), s_machine, Token);

        Assert.Equal(SequenceOutcome.Failed, result.Outcome);
        Assert.Empty(runner.Ran);
        Assert.Equal(new StepRunState(partition.Id, StepState.Failed, result.Error), result.State.Steps[0]);
    }

    [Fact]
    public async Task FailsAStepWhoseRunnerThrows()
    {
        PartitionStep partition = Partition();
        RunScriptStep throwing = Script(SequencePhase.WindowsPE);
        RunScriptStep timingOut = Script(SequencePhase.WindowsPE) with { ContinueOnError = true };
        ScriptedStepRunner runner = new ScriptedStepRunner()
            .On(timingOut, (_, _) => throw new TaskCanceledException("The request timed out."))
            .On(throwing, (_, _) => throw new InvalidOperationException("diskpart.exe is missing."));

        SequenceRunResult result = await Engine(runner).RunAsync(Start(partition, timingOut, throwing), s_machine, Token);

        Assert.Equal(SequenceOutcome.Failed, result.Outcome);
        Assert.Equal("diskpart.exe is missing.", result.Error);
        Assert.Equal(new StepRunState(timingOut.Id, StepState.Failed, "The request timed out."), result.State.Steps[1]);
        Assert.Equal("3 Done Failed Failed", Summary(result.State));
    }

    [Fact]
    public async Task StopsAndKeepsTheRunningMark()
    {
        PartitionStep partition = Partition();
        RunScriptStep script = Script(SequencePhase.WindowsPE);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        ScriptedStepRunner runner = new ScriptedStepRunner().On(script, async (_, cancellationToken) =>
        {
            await stop.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();

            return StepResult.Done();
        });
        BlobStore store = new();

        SequenceRunResult result = await Engine(runner, store).RunAsync(Start(partition, script), s_machine, stop.Token);

        Assert.Equal(SequenceOutcome.Stopped, result.Outcome);
        Assert.Null(result.Error);
        Assert.Equal("1 Done Running", Summary(result.State));
        Assert.Equal("1 Done Running", Summary(BlobStore.Load(store.Latest)));

        SequenceRunResult resumed = await Engine(new ScriptedStepRunner()).RunAsync(BlobStore.Load(store.Latest), s_machine, Token);

        Assert.Equal(SequenceEngine.InterruptedError, resumed.Error);
    }

    // A runner may end a stopped step as Failed rather than throw, such as a script whose timeout is linked to the stop.
    // The step then counts as interrupted, so a resumed run does not go on past it.
    [Fact]
    public async Task StopsAndKeepsTheRunningMarkWhenTheStepFailsAsTheStopCame()
    {
        PartitionStep partition = Partition();
        RunScriptStep script = Script(SequencePhase.WindowsPE);
        RunScriptStep next = Script(SequencePhase.WindowsPE);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        ScriptedStepRunner runner = new ScriptedStepRunner().On(script, async (_, _) =>
        {
            await stop.CancelAsync();

            return StepResult.Failed("The script was ended.");
        });
        BlobStore store = new();

        SequenceRunResult result = await Engine(runner, store).RunAsync(Start(partition, script, next), s_machine, stop.Token);

        Assert.Equal(SequenceOutcome.Stopped, result.Outcome);
        Assert.Null(result.Error);
        Assert.Equal("1 Done Running Pending", Summary(BlobStore.Load(store.Latest)));

        ScriptedStepRunner after = new();

        SequenceRunResult resumed = await Engine(after).RunAsync(BlobStore.Load(store.Latest), s_machine, Token);

        Assert.Equal(SequenceOutcome.Failed, resumed.Outcome);
        Assert.Equal(SequenceEngine.InterruptedError, resumed.Error);
        Assert.Empty(after.Ran);
    }

    [Fact]
    public async Task KeepsAStepThatFinishedAsTheStopCame()
    {
        PartitionStep partition = Partition();
        RunScriptStep next = Script(SequencePhase.WindowsPE);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        ScriptedStepRunner runner = new ScriptedStepRunner().On(partition, async (_, _) =>
        {
            await stop.CancelAsync();

            return StepResult.Done();
        });
        BlobStore store = new();

        SequenceRunResult result = await Engine(runner, store).RunAsync(Start(partition, next), s_machine, stop.Token);

        Assert.Equal(SequenceOutcome.Stopped, result.Outcome);
        Assert.Equal([partition.Id], runner.Ran);
        Assert.Equal("1 Done Pending", Summary(BlobStore.Load(store.Latest)));
    }

    // Runs the sequence until the step starts and returns the blob saved at that moment, as a power loss would leave it.
    private static async Task<string> BlobWhileRunningAsync(SequenceStep step, SequenceState start)
    {
        BlobStore store = new();
        string? blob = null;
        ScriptedStepRunner runner = new ScriptedStepRunner().On(step, (_, _) =>
        {
            blob = store.Latest;

            return Task.FromResult(StepResult.Done());
        });

        await Engine(runner, store).RunAsync(start, s_machine, Token);

        return blob ?? throw new InvalidOperationException("The step did not run.");
    }

    private static SequenceEngine Engine(ScriptedStepRunner runner, BlobStore? store = null, StepPercents? percents = null) =>
        new(runner, store ?? new BlobStore(), percents ?? new StepPercents());

    private static string Summary(SequenceState state) =>
        $"{state.NextIndex} {string.Join(' ', state.Steps.Select(step => step.State))}";

    private static Dictionary<string, string> Outputs(params (string Name, string Value)[] outputs) =>
        outputs.ToDictionary(output => output.Name, output => output.Value, StringComparer.Ordinal);

    private static SequenceDefinition Definition(params SequenceStep[] steps) => new(SequenceDefinition.CurrentVersion, steps);

    private static SequenceState Start(params SequenceStep[] steps) => SequenceStates.Start(Guid.NewGuid(), Definition(steps));

    private static PartitionStep Partition() => new() { Id = Guid.NewGuid(), Name = "Partition the disk" };

    private static ApplyImageStep ApplyImage() => new() { Id = Guid.NewGuid(), Name = "Apply the image", ImageId = Guid.NewGuid() };

    private static JoinDomainStep JoinDomain() => new() { Id = Guid.NewGuid(), Name = "Join the domain" };

    private static RunScriptStep Script(SequencePhase phase) =>
        new() { Id = Guid.NewGuid(), Name = "Run a script", Phase = phase, Script = "exit /b 0" };

    private static RebootStep Reboot() => new() { Id = Guid.NewGuid(), Name = "Restart" };
}
