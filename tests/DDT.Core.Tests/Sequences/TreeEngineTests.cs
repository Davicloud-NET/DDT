// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;
using static DDT.Core.Tests.Sequences.TreeFixture;

namespace DDT.Core.Tests.Sequences;

// The engine on trees. Every run goes through the JSON the agent writes, and the driver resumes after each restart and
// hand-over from the blob alone, in a fresh engine, as the agent does.
public sealed class TreeEngineTests
{
    private static readonly MachineVariables s_machine = new(
        "Dell Inc.",
        "Latitude 7440",
        "ABC1234",
        "4c4c4544-0042-3510-8052-b4c04f4d3232",
        ["00155D010203"],
        "PC-0042",
        SequencePhase.WindowsPE)
    {
        Variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Site"] = "Vienna" },
    };

    // Every step is done, and a restart asks for the restart.
    private static readonly Dictionary<Guid, Func<StepContext, StepResult>> s_noBehaviours = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // The fixture's tree through its restarts, its retry, its caught failure and the hand-over on one branch.
    [Fact]
    public async Task RunsATreeThroughItsRestartsAndTheHandOver()
    {
        TreeFixture tree = new();

        Driven run = await DriveAsync(tree.Start(), tree.Behaviours);

        Assert.Equal(SequenceOutcome.Completed, run.Result.Outcome);
        Assert.Null(run.Result.Error);
        Assert.Equal(
            [
                "Partition Done 1",
                "Set the office Done 1",
                "Prepare Done 1",
                "Prepare a Done 1",
                "Only on a Precision Skipped 1",
                "By model Done 1 Then",
                "Restarting in Then Done 1",
                "After the restart Done 1",
                "Not a Latitude Skipped 1",
                "By office Done 1 Else",
                "Berlin Skipped 1",
                "Elsewhere Done 1",
                "Retry Done 1 x2",
                "Try Done 2",
                "Restart in the retry Done 2",
                "At most twice Done 1 x2",
                "Twice Done 2",
                "Guarded Failed 1",
                "Failing Failed 1",
                "Never reached Skipped 1",
                "By site Done 1 Then",
                "In Windows Done 1",
                "In Windows PE Skipped 1",
                "Last Done 1",
            ],
            Lines(run.Result.State));
        Assert.Equal(
            [
                tree.Partition.Id, tree.PrepareA.Id, tree.RestartingInThen.Id, tree.AfterRestart.Id, tree.Elsewhere.Id,
                tree.Try.Id, tree.RestartInRetry.Id, tree.Try.Id, tree.RestartInRetry.Id, tree.Twice.Id, tree.Twice.Id,
                tree.Failing.Id, tree.InWindows.Id, tree.Last.Id,
            ],
            run.Runner.Ran);
        Assert.Equal(SequenceState.TreeFormat, run.Result.State.Format);
        Assert.Null(run.Result.State.Cursor);
    }

    // The run variables hold what steps set, and a step without a phase of its own runs in the phase the branch taken
    // led to, where conditions read the variables steps set on top of the run's values.
    [Fact]
    public async Task KeepsWhatStepsSetAndRunsEachStepInThePhaseItsBranchLedTo()
    {
        TreeFixture tree = new();

        Driven run = await DriveAsync(tree.Start(), tree.Behaviours);

        Assert.Equal(
            Outputs(
                (Office, "VIENNA-7440"),
                (MachineVariableNames.LastStepFailed, "No"),
                (Tries, "2"),
                (MachineVariableNames.LastExitCode, "5")),
            run.Result.State.Variables);
        Assert.Equal(
            "The script ended with exit code 5.",
            run.Result.State.Steps[Order(tree, tree.Guarded)].Error);
        Assert.Equal(SequencePhase.Windows, run.Runner.Runs.Single(ran => ran.StepId == tree.InWindows.Id).Context.Phase);
        Assert.Equal(SequencePhase.Windows, run.Runner.Runs.Single(ran => ran.StepId == tree.Last.Id).Context.Phase);
        Assert.Equal("VIENNA-7440", run.Runner.Runs.Single(ran => ran.StepId == tree.Elsewhere.Id).Context.Machine.Value(Office));
        Assert.Equal("Vienna", run.Runner.Runs.Single(ran => ran.StepId == tree.Elsewhere.Id).Context.Machine.Value("site"));
    }

    // Whatever save the machine stopped after, a fresh engine resumes from the blob alone and does what the run did. A
    // blob saved while a step that cannot run twice was running fails that step as interrupted first.
    [Fact]
    public async Task ResumesFromEverySavedStateInAFreshEngine()
    {
        TreeFixture tree = new();
        Driven reference = await DriveAsync(tree.Start(), tree.Behaviours);
        string finished = Json(reference.Result.State);
        int resumed = 0;
        int interrupted = 0;

        for (int blob = 0; blob < reference.Store.Blobs.Count; blob++)
        {
            SequenceState saved = BlobStore.Load(reference.Store.Blobs[blob]);

            if (RunningLeaf(saved) is { Resumable: false } running)
            {
                Driven failed = await DriveAsync(saved, tree.Behaviours);
                StepRunState first = BlobStore.Load(failed.Store.Blobs[0]).Steps[Order(tree, running)];

                Assert.Equal((StepState.Failed, SequenceEngine.InterruptedError), (first.State, first.Error));
                Assert.NotEqual(running.Id, failed.Runner.Ran.FirstOrDefault());
                interrupted++;

                continue;
            }

            Driven run = await DriveAsync(saved, tree.Behaviours);

            Assert.Equal(SequenceOutcome.Completed, run.Result.Outcome);
            Assert.Equal(finished, Json(run.Result.State));
            Assert.Equal(reference.Runner.Runs.Where(ran => ran.Mark >= blob).Select(ran => ran.StepId), run.Runner.Ran);

            // A blob saved for the hand-over is saved once more when the run resumes in the phase it left.
            IEnumerable<string> blobs = run.Store.Blobs.Count > 0 && run.Store.Blobs[0] == reference.Store.Blobs[blob]
                ? run.Store.Blobs.Skip(1)
                : run.Store.Blobs;
            Assert.Equal(reference.Store.Blobs.Skip(blob + 1), blobs);
            resumed++;
        }

        Assert.Equal(reference.Store.Blobs.Count, resumed + interrupted);
        Assert.True(resumed >= 15, $"Only {resumed} of {reference.Store.Blobs.Count} blobs were resumed.");
    }

    // The IF records the test that chose its branch, a skipped step the test that skipped it, and a repeat its Until.
    [Fact]
    public async Task RecordsTheTestsThatDecidedOnSkippedStepsIfsAndRepeats()
    {
        TreeFixture tree = new();

        SequenceState state = (await DriveAsync(tree.Start(), tree.Behaviours)).Result.State;

        Assert.Equal([new TestEvaluation("when", false, "Latitude 7440")], Run(state, tree, tree.OnlyPrecision).Evaluation);
        Assert.Equal([new TestEvaluation("test", true, "Latitude 7440")], Run(state, tree, tree.ByModel).Evaluation);
        Assert.Equal([new TestEvaluation("test", false, "VIENNA-7440")], Run(state, tree, tree.ByOffice).Evaluation);
        Assert.Equal([new TestEvaluation("until", true, "0")], Run(state, tree, tree.Retry).Evaluation);
        Assert.Equal([new TestEvaluation("until", false, "VIENNA-7440")], Run(state, tree, tree.AtMostTwice).Evaluation);
        Assert.Null(Run(state, tree, tree.Berlin).Evaluation);
        Assert.Null(Run(state, tree, tree.Partition).Evaluation);
    }

    [Fact]
    public async Task SkipsAContainerWhoseOwnConditionsDoNotHoldWithEverythingInIt()
    {
        RunScriptStep inside = Script("Inside");
        RunScriptStep deeper = Script("Deeper");
        GroupStep nested = new() { Id = Guid.NewGuid(), Name = "Nested", Steps = [deeper] };
        GroupStep group = new()
        {
            Id = Guid.NewGuid(),
            Name = "Only in Windows",
            When = new TestCondition(MachineVariableNames.Phase, ConditionOperator.Equals, "Windows"),
            Steps = [inside, nested],
        };
        RunScriptStep after = Script("After");

        Driven run = await DriveAsync(SequenceStates.Start(Guid.NewGuid(), Tree(group, after)), s_noBehaviours);

        Assert.Equal(SequenceOutcome.Completed, run.Result.Outcome);
        Assert.Equal([after.Id], run.Runner.Ran);
        Assert.Equal(
            ["Only in Windows Skipped 1", "Inside Skipped 1", "Nested Skipped 1", "Deeper Skipped 1", "After Done 1"],
            Lines(run.Result.State));
        Assert.Equal([new TestEvaluation("when", false, "WindowsPE")], run.Result.State.Steps[0].Evaluation);
    }

    [Fact]
    public async Task EntersAnEmptyBranchAndLeavesAtOnce()
    {
        RunScriptStep otherwise = Script("Otherwise");
        IfStep choice = new()
        {
            Id = Guid.NewGuid(),
            Name = "Choice",
            Test = new TestCondition(MachineVariableNames.Model, ConditionOperator.Contains, "Latitude"),
            Else = [otherwise],
        };
        GroupStep empty = new() { Id = Guid.NewGuid(), Name = "Empty" };

        Driven run = await DriveAsync(SequenceStates.Start(Guid.NewGuid(), Tree(choice, empty)), s_noBehaviours);

        Assert.Equal(SequenceOutcome.Completed, run.Result.Outcome);
        Assert.Empty(run.Runner.Ran);
        Assert.Equal(["Choice Done 1 Then", "Otherwise Skipped 1", "Empty Done 1"], Lines(run.Result.State));
    }

    // Taking Else, which has no step in Windows, the run stays in Windows PE, and the restart after the IF runs there.
    // Taking Then, the run is handed over at its step in Windows, with the IF's branch saved for the other phase.
    [Fact]
    public async Task ChangesThePhaseOnTheBranchThatNeedsItOnly()
    {
        RunScriptStep inWindows = Script("In Windows", SequencePhase.Windows);
        RunScriptStep inWindowsPE = Script("In Windows PE");
        IfStep choice = new()
        {
            Id = Guid.NewGuid(),
            Name = "Choice",
            Test = new TestCondition(MachineVariableNames.Model, ConditionOperator.Contains, "Precision"),
            Then = [inWindows],
            Else = [inWindowsPE],
        };
        RebootStep after = Reboot("After");
        ScriptedStepRunner runner = new();
        BlobStore store = new();

        SequenceRunResult result = await Engine(runner, store)
            .RunAsync(SequenceStates.Start(Guid.NewGuid(), Tree(choice, after)), s_machine, Token);

        Assert.Equal(SequenceOutcome.Completed, result.Outcome);
        Assert.Equal([inWindowsPE.Id, after.Id], runner.Ran);
        Assert.All(runner.Runs, ran => Assert.Equal(SequencePhase.WindowsPE, ran.Context.Phase));
        Assert.Equal(["Choice Done 1 Else", "In Windows Skipped 1", "In Windows PE Done 1", "After Done 1"], Lines(result.State));

        IfStep latitude = choice with { Test = new TestCondition(MachineVariableNames.Model, ConditionOperator.Contains, "Latitude") };
        SequenceRunResult other = await Engine(new ScriptedStepRunner(), store)
            .RunAsync(SequenceStates.Start(Guid.NewGuid(), Tree(latitude, after)), s_machine, Token);

        Assert.Equal(SequenceOutcome.PhaseChangeRequired, other.Outcome);
        Assert.Equal(new NodeCursor(inWindows.Id, false), BlobStore.Load(store.Latest).Cursor);
        Assert.Equal(
            ["Choice Running 1 Then", "In Windows Pending 0", "In Windows PE Skipped 1", "After Pending 0"],
            Lines(BlobStore.Load(store.Latest)));
    }

    [Fact]
    public async Task FailsARepeatAtItsLimitNamingHowOftenItRan()
    {
        RunScriptStep body = Script("Body");
        RepeatStep repeat = new()
        {
            Id = Guid.NewGuid(),
            Name = "Wait for the network",
            Until = new TestCondition(MachineVariableNames.IPv4Address, ConditionOperator.Exists),
            MaxTimes = 3,
            Steps = [body],
        };
        RunScriptStep after = Script("After");

        Driven run = await DriveAsync(SequenceStates.Start(Guid.NewGuid(), Tree(repeat, after)), s_noBehaviours);

        Assert.Equal(SequenceOutcome.Failed, run.Result.Outcome);
        Assert.Equal(
            "The repeat Wait for the network ran 3 times, the most it may, and its condition to stop never held.",
            run.Result.Error);
        Assert.Equal([body.Id, body.Id, body.Id], run.Runner.Ran);
        Assert.Equal(["Wait for the network Failed 1 x3", "Body Done 3", "After Pending 0"], Lines(BlobStore.Load(run.Store.Latest)));

        RepeatStep onceOnly = repeat with { MaxTimes = 1, ContinueOnError = true };
        Driven once = await DriveAsync(SequenceStates.Start(Guid.NewGuid(), Tree(onceOnly, after)), s_noBehaviours);

        Assert.Equal(SequenceOutcome.Completed, once.Result.Outcome);
        Assert.Equal(
            "The repeat Wait for the network ran once, the most it may, and its condition to stop never held.",
            once.Result.State.Steps[0].Error);
        Assert.Equal(["Wait for the network Failed 1 x1", "Body Done 1", "After Done 1"], Lines(once.Result.State));
    }

    // Do ... until: the body runs before Until is tested, and a failing script that goes on lets LastStepFailed decide.
    [Fact]
    public async Task RetriesUntilTheLastStepDidNotFail()
    {
        RunScriptStep attempt = Script("Attempt") with { ContinueOnError = true };
        RepeatStep retry = new()
        {
            Id = Guid.NewGuid(),
            Name = "Retry",
            Until = new AllCondition { Parts = [new TestCondition(MachineVariableNames.LastStepFailed, ConditionOperator.Equals, "no")] },
            MaxTimes = 5,
            Steps = [attempt],
        };
        Dictionary<Guid, Func<StepContext, StepResult>> behaviours = new()
        {
            [attempt.Id] = context =>
            {
                int tries = Count(context) + 1;
                StepResult result = tries < 3
                    ? new StepResult(StepOutcome.Failed, $"The script ended with exit code {tries}.", Outputs((Tries, $"{tries}")))
                    : StepResult.Done(Outputs((Tries, $"{tries}")));

                return result with { ExitCode = tries < 3 ? tries : 0 };
            },
        };

        Driven run = await DriveAsync(SequenceStates.Start(Guid.NewGuid(), Tree(retry)), behaviours);

        Assert.Equal(SequenceOutcome.Completed, run.Result.Outcome);
        Assert.Equal(["Retry Done 1 x3", "Attempt Done 3"], Lines(run.Result.State));
        Assert.Equal("No", run.Result.State.Variables[MachineVariableNames.LastStepFailed]);
        Assert.Equal("0", run.Result.State.Variables[MachineVariableNames.LastExitCode]);
        Assert.Equal(
            [("Yes", "1"), ("Yes", "2"), ("No", "0")],
            run.Store.States
                .Where(state => state.Steps[0].State == StepState.Running && state.Steps[1].State is StepState.Done or StepState.Failed)
                .Select(state => (
                    state.Variables[MachineVariableNames.LastStepFailed],
                    state.Variables[MachineVariableNames.LastExitCode])));
    }

    [Fact]
    public async Task FailsAnInterruptedStepInsideARepeatAndGoesOnWhereAnAncestorAllows()
    {
        RunScriptStep first = Script("First");
        RunScriptStep interrupted = Script("Interrupted");
        RunScriptStep rest = Script("Rest");
        RepeatStep repeat = new()
        {
            Id = Guid.NewGuid(),
            Name = "Repeat",
            Until = new TestCondition(MachineVariableNames.Model, ConditionOperator.Contains, "Latitude"),
            Steps = [first, interrupted, rest],
        };
        GroupStep guarded = new() { Id = Guid.NewGuid(), Name = "Guarded", ContinueOnError = true, Steps = [repeat] };
        RunScriptStep after = Script("After");
        SequenceState start = SequenceStates.Start(Guid.NewGuid(), Tree(guarded, after));
        string atPowerLoss = await BlobWhileRunningAsync(interrupted, start);

        Assert.Equal(
            ["Guarded Running 1", "Repeat Running 1 x1", "First Done 1", "Interrupted Running 1", "Rest Pending 0", "After Pending 0"],
            Lines(BlobStore.Load(atPowerLoss)));

        Driven run = await DriveAsync(BlobStore.Load(atPowerLoss), s_noBehaviours);

        Assert.Equal(SequenceOutcome.Completed, run.Result.Outcome);
        Assert.Equal([after.Id], run.Runner.Ran);
        Assert.Equal(
            ["Guarded Failed 1", "Repeat Failed 1 x1", "First Done 1", "Interrupted Failed 1", "Rest Skipped 1", "After Done 1"],
            Lines(run.Result.State));
        Assert.All(
            run.Result.State.Steps.Where(step => step.State == StepState.Failed),
            step => Assert.Equal(SequenceEngine.InterruptedError, step.Error));
        Assert.Equal("Yes", BlobStore.Load(run.Store.Blobs[0]).Variables[MachineVariableNames.LastStepFailed]);

        // Without an ancestor that goes on, the run fails as a flat one does, and the rest stays Pending.
        SequenceState unguarded = SequenceStates.Start(Guid.NewGuid(), Tree(guarded with { ContinueOnError = false }, after));
        Driven failed = await DriveAsync(BlobStore.Load(await BlobWhileRunningAsync(interrupted, unguarded)), s_noBehaviours);

        Assert.Equal(SequenceOutcome.Failed, failed.Result.Outcome);
        Assert.Equal(SequenceEngine.InterruptedError, failed.Result.Error);
        Assert.Empty(failed.Runner.Ran);
        Assert.Equal(
            ["Guarded Failed 1", "Repeat Failed 1 x1", "First Done 1", "Interrupted Failed 1", "Rest Pending 0", "After Pending 0"],
            Lines(BlobStore.Load(failed.Store.Latest)));
    }

    // Running twice does a Set variable or a Pause no harm, so either goes on with the visit it was saved in.
    [Fact]
    public async Task RunsAResumableStepFoundRunningAgain()
    {
        SetVariableStep set = new() { Id = Guid.NewGuid(), Name = "Set", Variable = "office", Value = "{{Site|upper}}" };
        PauseStep pause = new() { Id = Guid.NewGuid(), Name = "Pause", Message = "Check the cables." };
        SequenceState start = SequenceStates.Start(Guid.NewGuid(), Tree(set, pause));

        BlobStore store = new() { FailWhen = state => state.Steps[0].State == StepState.Done };
        SequenceRunResult unsaved = await Engine(new ScriptedStepRunner(), store).RunAsync(start, s_machine, Token);

        Assert.Equal(SequenceOutcome.Failed, unsaved.Outcome);
        Assert.Equal(["Set Running 1", "Pause Pending 0"], Lines(BlobStore.Load(store.Latest)));

        Driven setAgain = await DriveAsync(BlobStore.Load(store.Latest), s_noBehaviours);

        Assert.Equal(SequenceOutcome.Completed, setAgain.Result.Outcome);
        Assert.Equal(["Set Done 1", "Pause Done 1"], Lines(setAgain.Result.State));
        Assert.Equal("VIENNA", setAgain.Result.State.Variables[Office]);

        string pausing = await BlobWhileRunningAsync(pause, start);
        Driven pauseAgain = await DriveAsync(BlobStore.Load(pausing), s_noBehaviours);

        Assert.Equal([pause.Id], pauseAgain.Runner.Ran);
        Assert.Equal(["Set Done 1", "Pause Done 1"], Lines(pauseAgain.Result.State));
        Assert.Equal(StepState.Done, BlobStore.Load(pauseAgain.Store.Blobs[0]).Steps[1].State);
    }

    [Fact]
    public async Task SetsOnlyAVariableTheSequenceLetsStepsSet()
    {
        SetVariableStep undeclared = new() { Id = Guid.NewGuid(), Name = "Set", Variable = "Unknown", Value = "x", ContinueOnError = true };
        SetVariableStep fixedValue = undeclared with { Id = Guid.NewGuid(), Name = "Set fixed", Variable = "Fixed" };
        SetVariableStep missing = undeclared with
        {
            Id = Guid.NewGuid(),
            Name = "Set missing",
            Variable = Office,
            Value = "PC-{{AssetTag}}",
        };
        SequenceDefinition definition = Tree(undeclared, fixedValue, missing) with
        {
            Variables = [new VariableDeclaration { Name = Office, SetBySteps = true }, new VariableDeclaration { Name = "Fixed" }],
        };

        Driven run = await DriveAsync(SequenceStates.Start(Guid.NewGuid(), definition), s_noBehaviours);

        Assert.Equal(SequenceOutcome.Completed, run.Result.Outcome);
        Assert.Equal(
            [
                "Unknown cannot be set: the sequence does not declare it as a variable that steps may set.",
                "Fixed cannot be set: the sequence does not declare it as a variable that steps may set.",
                "Office cannot be set: The machine has no value for {{AssetTag}}.",
            ],
            run.Result.State.Steps.Select(step => step.Error));
        Assert.False(run.Result.State.Variables.ContainsKey(Office));
        Assert.Equal("Yes", run.Result.State.Variables[MachineVariableNames.LastStepFailed]);
    }

    // A stop in the second time through a repeat keeps the Running mark and the iteration, and the resumed run fails
    // the step as interrupted.
    [Fact]
    public async Task StopsInsideARepeatAndKeepsTheRunningMark()
    {
        RunScriptStep body = Script("Body");
        RepeatStep repeat = new()
        {
            Id = Guid.NewGuid(),
            Name = "Repeat",
            Until = new TestCondition(MachineVariableNames.Model, ConditionOperator.Contains, "Precision"),
            MaxTimes = 5,
            Steps = [body],
        };
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        int runs = 0;
        ScriptedStepRunner runner = new ScriptedStepRunner().On(body, async (_, cancellationToken) =>
        {
            if (++runs == 2)
            {
                await stop.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return StepResult.Done();
        });
        BlobStore store = new();

        SequenceRunResult result = await Engine(runner, store)
            .RunAsync(SequenceStates.Start(Guid.NewGuid(), Tree(repeat)), s_machine, stop.Token);

        Assert.Equal(SequenceOutcome.Stopped, result.Outcome);
        Assert.Null(result.Error);
        Assert.Equal(["Repeat Running 1 x2", "Body Running 2"], Lines(result.State));
        Assert.Equal(Json(result.State), store.Latest);

        SequenceRunResult resumed = await Engine(new ScriptedStepRunner()).RunAsync(BlobStore.Load(store.Latest), s_machine, Token);

        Assert.Equal(SequenceOutcome.Failed, resumed.Outcome);
        Assert.Equal(SequenceEngine.InterruptedError, resumed.Error);
        Assert.Equal(["Repeat Failed 1 x2", "Body Failed 2"], Lines(resumed.State));
    }

    // A failure goes up through the containers to the nearest one that lets the run go on, and the run goes on after
    // it.
    [Fact]
    public async Task GoesOnAfterTheNearestAncestorThatAllowsAFailure()
    {
        RunScriptStep failing = Script("Failing");
        RunScriptStep skipped = Script("Skipped");
        GroupStep inner = new() { Id = Guid.NewGuid(), Name = "Inner", Steps = [failing, skipped] };
        RunScriptStep sibling = Script("Sibling");
        GroupStep outer = new() { Id = Guid.NewGuid(), Name = "Outer", ContinueOnError = true, Steps = [inner, sibling] };
        GroupStep top = new() { Id = Guid.NewGuid(), Name = "Top", Steps = [outer] };
        RunScriptStep after = Script("After");
        Dictionary<Guid, Func<StepContext, StepResult>> behaviours = new()
        {
            [failing.Id] = _ => StepResult.Failed("diskpart.exe is missing."),
        };

        Driven run = await DriveAsync(SequenceStates.Start(Guid.NewGuid(), Tree(top, after)), behaviours);

        Assert.Equal(SequenceOutcome.Completed, run.Result.Outcome);
        Assert.Equal([failing.Id, after.Id], run.Runner.Ran);
        Assert.Equal(
            [
                "Top Done 1", "Outer Failed 1", "Inner Failed 1", "Failing Failed 1", "Skipped Skipped 1", "Sibling Skipped 1",
                "After Done 1",
            ],
            Lines(run.Result.State));
        Assert.Equal("diskpart.exe is missing.", run.Result.State.Steps[1].Error);
    }

    // A file an agent of version 1 or 2 wrote: the run goes on at NextIndex and saves Format 1 again, changing only
    // what the engine of version 2 changed, without a member of a tree and without the run variables of version 3.
    [Fact]
    public async Task ResumesAFormat1FileAsBefore()
    {
        PartitionStep partition = new() { Id = Guid.NewGuid(), Name = "Partition", RebootAfter = true };
        RunScriptStep precision = Script("Only on a Precision") with
        {
            Conditions = [new StepCondition(MachineVariableNames.Model, ConditionOperator.StartsWith, "Precision")],
        };
        RebootStep restart = Reboot("Restart");
        SequenceState written = SequenceStates.Start(Guid.NewGuid(), new SequenceDefinition(2, [partition, precision, restart])) with
        {
            NextIndex = 1,
            Steps = [Flat(partition, StepState.Done), Flat(precision, StepState.Pending), Flat(restart, StepState.Pending)],
            Variables = Outputs(("ddt.disk.windows", "7c1f")),
        };
        ScriptedStepRunner runner = new();
        BlobStore store = new();

        SequenceRunResult result = await Engine(runner, store).RunAsync(BlobStore.Load(Json(written)), s_machine, Token);

        Assert.Equal(SequenceOutcome.Completed, result.Outcome);
        Assert.Equal([restart.Id], runner.Ran);
        Assert.Equal(
            [
                Json(written with
                {
                    NextIndex = 2,
                    Steps = [Flat(partition, StepState.Done), Flat(precision, StepState.Skipped), Flat(restart, StepState.Pending)],
                }),
                Json(written with
                {
                    NextIndex = 2,
                    Steps = [Flat(partition, StepState.Done), Flat(precision, StepState.Skipped), Flat(restart, StepState.Running)],
                }),
                Json(written with
                {
                    NextIndex = 3,
                    Steps = [Flat(partition, StepState.Done), Flat(precision, StepState.Skipped), Flat(restart, StepState.Done)],
                }),
            ],
            store.Blobs);
        Assert.All(store.Blobs, blob => Assert.DoesNotContain("\"cursor\"", blob, StringComparison.Ordinal));
        Assert.All(store.Blobs, blob => Assert.DoesNotContain("\"pass\"", blob, StringComparison.Ordinal));
        Assert.All(store.Blobs, blob => Assert.DoesNotContain(MachineVariableNames.LastStepFailed, blob, StringComparison.Ordinal));
        Assert.Equal(store.Latest, Json(result.State));
    }

    // The engine runs a step without a phase of its own in the phase the run is in, which for a flat document is the
    // phase SequencePhases gives it, even after a skipped step of the other phase, since the phase is checked first.
    [Fact]
    public async Task RunsAFlatDocumentInThePhasesSequencePhasesGives()
    {
        RunScriptStep precisionOnly = Script("Only on a Precision", SequencePhase.Windows) with
        {
            Conditions = [new StepCondition(MachineVariableNames.Model, ConditionOperator.StartsWith, "Precision")],
        };
        SequenceDefinition flat = new(
            2,
            [
                Script("In Windows PE"),
                Reboot("Restart in Windows PE"),
                precisionOnly,
                Reboot("Restart in Windows"),
                Script("In Windows", SequencePhase.Windows),
            ]);

        Driven run = await DriveAsync(SequenceStates.Start(Guid.NewGuid(), flat), s_noBehaviours);

        Assert.Equal(SequenceOutcome.Completed, run.Result.Outcome);
        Assert.Equal(
            [
                "In Windows PE Done 0", "Restart in Windows PE Done 0", "Only on a Precision Skipped 0", "Restart in Windows Done 0",
                "In Windows Done 0",
            ],
            Lines(run.Result.State));
        Assert.All(
            run.Runner.Runs,
            ran => Assert.Equal(SequencePhases.Of(flat, SequenceTree.Index(flat)[ran.StepId].Order), ran.Context.Phase));
        Assert.All(run.Store.States, state => Assert.Equal((SequenceState.CurrentFormat, null), (state.Format, state.Cursor)));
    }

    // Runs the sequence until the step starts and returns the blob saved at that moment, as a power loss would leave
    // it.
    private static async Task<string> BlobWhileRunningAsync(SequenceStep step, SequenceState start)
    {
        BlobStore store = new();
        string? blob = null;
        ScriptedStepRunner runner = new ScriptedStepRunner().On(step, (_, _) =>
        {
            blob ??= store.Latest;

            return Task.FromResult(StepResult.Done());
        });

        await Engine(runner, store).RunAsync(start, s_machine, Token);

        return blob ?? throw new InvalidOperationException("The step did not run.");
    }

    // Runs as the agent does: after a restart from the latest blob, after a hand-over from it in the other phase.
    private static async Task<Driven> DriveAsync(SequenceState state, IReadOnlyDictionary<Guid, Func<StepContext, StepResult>> behaviours)
    {
        BlobStore store = new();
        RecordingRunner runner = new(behaviours, store);

        for (int start = 0; start < 50; start++)
        {
            SequenceRunResult result = await Engine(runner, store).RunAsync(state, s_machine, Token);

            switch (result.Outcome)
            {
                case SequenceOutcome.RebootRequired:
                    state = BlobStore.Load(store.Latest);
                    break;
                case SequenceOutcome.PhaseChangeRequired:
                    SequenceState staged = BlobStore.Load(store.Latest);
                    SequencePhase other = staged.Phase == SequencePhase.WindowsPE ? SequencePhase.Windows : SequencePhase.WindowsPE;
                    state = staged with { Phase = other };
                    break;
                default:
                    return new Driven(result, store, runner);
            }
        }

        throw new InvalidOperationException("The run did not end.");
    }

    private static StepRunState Flat(SequenceStep step, StepState state) => new(step.Id, state, null);

    private static SequenceEngine Engine(IStepRunner runner, BlobStore? store = null) =>
        new(runner, store ?? new BlobStore(), new StepPercents());

    private static SequenceStep? RunningLeaf(SequenceState state) =>
        SequenceTree.Nodes(state.Definition)
            .Zip(state.Steps)
            .Where(pair => !pair.First.IsContainer && pair.Second.State == StepState.Running)
            .Select(pair => pair.First)
            .FirstOrDefault();

    private static string[] Lines(SequenceState state) =>
    [
        .. SequenceTree.Nodes(state.Definition).Zip(state.Steps, (node, run) =>
            $"{node.Name} {run.State} {run.Pass}{(run.Iteration > 0 ? $" x{run.Iteration}" : "")}{(run.Branch is { } b ? $" {b}" : "")}"),
    ];

    private static int Order(TreeFixture tree, SequenceStep node) => SequenceTree.Index(tree.Definition)[node.Id].Order;

    private static StepRunState Run(SequenceState state, TreeFixture tree, SequenceStep node) => state.Steps[Order(tree, node)];

    private static string Json(SequenceState state) => JsonSerializer.Serialize(state, AgentJsonContext.Default.SequenceState);

    private sealed record Driven(SequenceRunResult Result, BlobStore Store, RecordingRunner Runner);
}
