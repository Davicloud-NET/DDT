// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;
using static DDT.Core.Tests.Sequences.TreeSteps;

namespace DDT.Core.Tests.Sequences;

// The rules of a sequence hold on every path through its tree.
public sealed class SequenceValidatorTreeTests
{
    private static IReadOnlyList<SequencePhase> PhasesOf(SequenceDefinition definition, SequenceStep node) =>
        Assert.Single(SequenceValidator.Analyse(definition).NodePhases, phases => phases.NodeId == node.Id).Phases;

    // A flat sequence has one path: the validator reports what a list's checks would, in the order of the steps.
    [Fact]
    public void SaysOfAFlatSequenceWhatItAlwaysSaid()
    {
        RebootStep reboot = Reboot();
        RunScriptStep package = Script(SequencePhase.WindowsPE) with { PackageId = Guid.NewGuid(), RebootExitCodes = [3010] };
        PartitionStep partition = Partition() with
        {
            Conditions = [new StepCondition(MachineVariableNames.Model, ConditionOperator.Equals, "Latitude 5440")],
        };
        ApplyImageStep apply = ApplyImage() with { ContinueOnError = true };
        RunScriptStep late = Script(SequencePhase.WindowsPE);

        Assert.Equal(
            [
                (reboot.Id, null, "sequence.restartBeforePartition"),
                (package.Id, "packageId", "sequence.packageBeforePartition"),
                (package.Id, "rebootExitCodes", "sequence.restartBeforePartition"),
                (partition.Id, "conditions", "sequence.cannotSkip"),
                (apply.Id, "continueOnError", "sequence.cannotGoOn"),
                (late.Id, "phase", "sequence.windowsPEAfterWindows"),
            ],
            Said(Validate(new SequenceDefinition(1, [reboot, package, partition, apply, Script(SequencePhase.Windows), late]))));

        RunScriptStep restarting = Script(SequencePhase.WindowsPE) with { RebootAfter = true, RebootExitCodes = [3010] };
        WriteCloudInitSeedStep again = WriteSeed();
        PartitionStep windowsStep = Partition();
        RunScriptStep inWindows = Script(SequencePhase.Windows);

        Assert.Equal(
            [
                (restarting.Id, "rebootAfter", "sequence.restartWithRawImage"),
                (restarting.Id, "rebootExitCodes", "sequence.restartWithRawImage"),
                (again.Id, null, "sequence.oneSeed"),
                (windowsStep.Id, null, "sequence.windowsWithRawImage"),
                (inWindows.Id, "phase", "sequence.windowsWithRawImage"),
            ],
            Said(Validate(new SequenceDefinition(2, [restarting, WriteRawImage(), WriteSeed(), again, windowsStep, inWindows]))));
    }

    // A different image per model is what an IF is for.
    [Fact]
    public void AcceptsADifferentImageOnEachBranch()
    {
        Assert.Empty(Validate(
            Partition(),
            If([ApplyImage()], [ApplyImage()]),
            InjectDrivers(),
            WriteUnattend(),
            Script(SequencePhase.Windows),
            JoinDomain()));
        Assert.Empty(Validate(If([Partition(), ApplyImage()], [Partition(), ApplyImage()]), Reboot(), Script(SequencePhase.Windows)));
    }

    [Fact]
    public void RefusesAnImageOnOnlyOneBranchBeforeWindowsOrTheDrivers()
    {
        RunScriptStep script = Script(SequencePhase.Windows);
        InjectDriversStep drivers = InjectDrivers();

        AssertOnly(Validate(Partition(), If([ApplyImage()]), script), script, "phase", "sequence.windowsNeedsImageOnEveryPath");
        AssertOnly(Validate(Partition(), If([ApplyImage()]), drivers), drivers, null, "sequence.driversBeforeImage");
    }

    [Fact]
    public void RefusesAStepInWindowsPEAfterWindowsOnOnePath()
    {
        RunScriptStep late = Script(SequencePhase.WindowsPE);

        AssertOnly(
            Validate(Partition(), ApplyImage(), If([Script(SequencePhase.Windows)]), late),
            late,
            "phase",
            "sequence.windowsPEAfterWindows");

        // A restart runs in the phase its path is in: Windows after Then, Windows PE after Else, and both are fine.
        Assert.Empty(Validate(Partition(), ApplyImage(), If([Script(SequencePhase.Windows)]), Reboot()));
    }

    [Fact]
    public void RefusesARestartBeforePartitioningOnOnePath()
    {
        RebootStep reboot = Reboot();
        RunScriptStep restarting = Script(SequencePhase.WindowsPE) with { RebootAfter = true };

        AssertOnly(Validate(If([Partition()]), reboot), reboot, null, "sequence.restartBeforePartition");
        AssertOnly(Validate(If([Partition()]), restarting), restarting, "rebootAfter", "sequence.restartBeforePartition");
        Assert.Empty(Validate(If([Partition()], [Partition()]), Reboot()));
    }

    [Fact]
    public void RefusesTwoImagesOnOnePath()
    {
        ApplyImageStep again = ApplyImage();

        AssertOnly(Validate(Partition(), ApplyImage(), If([again])), again, null, "sequence.oneImage");
    }

    // Only a step's own conditions are refused on the steps a run relies on; a group's make the paths.
    [Fact]
    public void SkipsAGroupWithConditionsWithEverythingInIt()
    {
        RunScriptStep script = Script(SequencePhase.Windows);
        GroupStep conditional = Group(ApplyImage()) with { When = ThinkPad };
        ApplyImageStep ownWhen = ApplyImage() with { When = ThinkPad };

        AssertOnly(Validate(Partition(), conditional, script), script, "phase", "sequence.windowsNeedsImageOnEveryPath");
        Assert.Empty(Validate(Partition(), Group(ApplyImage()), script));
        AssertOnly(Validate(Partition(), ownWhen), ownWhen, "when", "sequence.cannotSkip");
    }

    // A failure goes on after the group, so what follows the failing step in it may not have happened.
    [Fact]
    public void GoesOnAfterAGroupThatContinuesOnErrorFromWhereAStepFailed()
    {
        RunScriptStep script = Script(SequencePhase.Windows);
        GroupStep goingOn = Group(Partition(), ApplyImage()) with { ContinueOnError = true };

        AssertOnly(Validate(goingOn, script), script, "phase", "sequence.windowsNeedsImageOnEveryPath");
        Assert.Empty(Validate(Group(Partition(), ApplyImage()), script));
    }

    [Fact]
    public void DecidesAnIfByItsTestAlone()
    {
        IfStep withWhen = If([Reboot()]) with { When = ThinkPad };
        IfStep withConditions = If([Reboot()]) with
        {
            Conditions = [new StepCondition(MachineVariableNames.Model, ConditionOperator.Equals, "Latitude 7440")],
        };
        IfStep withoutTest = If([Reboot()]) with { Test = null! };

        AssertOnly(Validate(Partition(), withWhen), withWhen, "when", "sequence.ifOnlyTest");
        AssertOnly(Validate(Partition(), withConditions), withConditions, "conditions", "sequence.ifOnlyTest");
        AssertOnly(Validate(Partition(), withoutTest), withoutTest, "test", "sequence.ifNeedsTest");
    }

    [Fact]
    public void AcceptsARepeatWithRestartsInside()
    {
        Assert.Empty(Validate(Partition(), ApplyImage(), Script(SequencePhase.Windows), Repeat(Script(SequencePhase.Windows), Reboot())));
        Assert.Empty(Validate(Partition(), Repeat(Script(SequencePhase.WindowsPE), Reboot() with { When = ThinkPad })));
    }

    [Fact]
    public void RefusesAStepARunDoesOnceInsideARepeat()
    {
        foreach (SequenceStep once in new SequenceStep[] { Partition(), ApplyImage(), InjectDrivers(), WriteUnattend() })
        {
            AssertOnly(Validate(Partition(), ApplyImage(), Repeat(once)), once, null, "sequence.onceInRepeat");
        }

        JoinDomainStep join = JoinDomain();
        WriteCloudInitSeedStep seed = WriteSeed();
        WriteRawImageStep raw = WriteRawImage();

        AssertOnly(Validate(Partition(), ApplyImage(), Script(SequencePhase.Windows), Repeat(join)), join, null, "sequence.onceInRepeat");
        AssertOnly(Validate(WriteRawImage(), Repeat(Group(seed))), seed, null, "sequence.onceInRepeat");
        AssertOnly(Validate(Repeat(raw)), raw, null, "sequence.onceInRepeat");
    }

    [Fact]
    public void RefusesAPhaseChangeInsideARepeat()
    {
        RunScriptStep inWindows = Script(SequencePhase.Windows);
        RunScriptStep nested = Script(SequencePhase.Windows);

        AssertOnly(Validate(Partition(), ApplyImage(), Repeat(inWindows)), inWindows, "phase", "sequence.phaseChangeInRepeat");

        // Said once, however many repeats hold the step, and not again as Windows PE after Windows the next time round.
        AssertOnly(
            Validate(Partition(), ApplyImage(), Repeat(Script(SequencePhase.WindowsPE), Repeat(nested))),
            nested,
            "phase",
            "sequence.phaseChangeInRepeat");
    }

    [Fact]
    public void NeedsUntilAndOneToAHundredTimesForARepeat()
    {
        RepeatStep noUntil = Repeat(Reboot()) with { Until = null! };

        AssertOnly(Validate(Partition(), noUntil), noUntil, "until", "sequence.repeatNeedsUntil");

        foreach (int times in new[] { 0, SequenceValidator.MaxRepeatTimes + 1 })
        {
            RepeatStep wrong = Repeat(Reboot()) with { MaxTimes = times };

            AssertOnly(Validate(Partition(), wrong), wrong, "maxTimes", "sequence.repeatTimes");
        }

        Assert.Empty(Validate(Partition(), Repeat(Reboot()) with { MaxTimes = SequenceValidator.MaxRepeatTimes }));
    }

    // At its limit a repeat fails unless it goes on, and the run goes on after the nearest group that continues on
    // error, still in Windows PE.
    [Fact]
    public void GoesOnFromARepeatThatFailsAtItsLimit()
    {
        RunScriptStep tries = Script(SequencePhase.WindowsPE) with { ContinueOnError = true };
        RebootStep reboot = Reboot();

        SequenceDefinition Sequence(bool goOnAtLimit) => Definition(
            Partition(),
            ApplyImage(),
            Group(Repeat(tries) with { GoOnAtLimit = goOnAtLimit }, Script(SequencePhase.Windows)) with { ContinueOnError = true },
            reboot);

        Assert.Equal([SequencePhase.WindowsPE, SequencePhase.Windows], PhasesOf(Sequence(goOnAtLimit: false), reboot));
        Assert.Equal([SequencePhase.Windows], PhasesOf(Sequence(goOnAtLimit: true), reboot));
    }

    [Fact]
    public void NestsAtMostEightLevels()
    {
        static SequenceStep Nest(int groups, SequenceStep inside) => groups == 0 ? inside : Group(Nest(groups - 1, inside));

        RebootStep deepest = Reboot();
        RebootStep below = Reboot();

        Assert.Empty(Validate(Partition(), Nest(SequenceValidator.MaxDepth - 1, Reboot())));
        AssertOnly(Validate(Partition(), Nest(SequenceValidator.MaxDepth, deepest)), deepest, null, "sequence.tooDeep");

        // Once, at the first node too deep.
        GroupStep tooDeep = (GroupStep)Nest(1, below);
        Assert.Single(Validate(Partition(), Nest(SequenceValidator.MaxDepth, tooDeep)), problem => problem.Code == "sequence.tooDeep");
    }

    [Fact]
    public void CountsTheStepsInsideContainersAndEveryNode()
    {
        GroupStep tooManySteps = Group([.. Enumerable.Range(0, SequenceValidator.MaxSteps).Select(_ => Reboot())]);

        AssertOnly(Validate(Partition(), tooManySteps), null, "steps", "sequence.stepCount");
        Assert.Empty(Validate(Partition(), tooManySteps with { Steps = tooManySteps.Steps.Skip(1).ToArray() }));

        // A group holds no steps of its own.
        AssertOnly(Validate(Group()), null, "steps", "sequence.stepCount");

        SequenceStep[] nodes = [Partition(), .. Enumerable.Range(0, SequenceValidator.MaxNodes).Select(_ => Group())];
        AssertOnly(Validate(nodes), null, "steps", "sequence.nodeCount");
        Assert.Empty(Validate(nodes[..SequenceValidator.MaxNodes]));
    }

    [Fact]
    public void SaysWhichContainerHasAnEmptyPlace()
    {
        GroupStep group = Group(Reboot(), null!);
        IfStep branch = If([Reboot()], [null!]);

        Assert.Equal(
            [(group.Id, "steps", "sequence.stepEmpty"), (branch.Id, "else", "sequence.stepEmpty")],
            Said(Validate(Partition(), group, branch)));
    }

    [Fact]
    public void WarnsOfContainersThatDoNothing()
    {
        GroupStep group = Group();
        IfStep branch = If([]);
        RepeatStep repeat = Repeat();

        SequenceAnalysis analysis = SequenceValidator.Analyse(Definition(Partition(), group, branch, repeat));

        Assert.Empty(analysis.Problems);
        Assert.Equal(
            [
                (group.Id, null, "sequence.emptyContainerWarning"),
                (branch.Id, null, "sequence.emptyContainerWarning"),
                (repeat.Id, null, "sequence.emptyContainerWarning"),
            ],
            Said(analysis.Warnings));
        Assert.Equal(["group", "if", "repeat"], analysis.Warnings.Select(warning => (string)warning.Args!["kind"]));
    }

    [Fact]
    public void WarnsOfAPauseWhereARestartWouldEndTheRun()
    {
        PauseStep early = Pause();
        PauseStep oneBranch = Pause();
        PauseStep raw = Pause();

        SequenceAnalysis before = SequenceValidator.Analyse(Definition(early, Partition()));

        Assert.Empty(before.Problems);
        AssertOnly(before.Warnings, early, null, "sequence.pauseBeforePartitionWarning");
        AssertOnly(SequenceValidator.Analyse(Definition(If([Partition()]), oneBranch)).Warnings, oneBranch, null, "sequence.pauseBeforePartitionWarning");
        AssertOnly(SequenceValidator.Analyse(Definition(WriteRawImage(), raw)).Warnings, raw, null, "sequence.pauseBeforePartitionWarning");
        Assert.Empty(SequenceValidator.Analyse(Definition(Partition(), Pause())).Warnings);
        Assert.Empty(SequenceValidator.Analyse(Definition(Partition(), ApplyImage(), Script(SequencePhase.Windows), Pause())).Warnings);
    }

    // Each node in the order of SequenceTree.Nodes, a container with the phases of its start and of what it holds.
    [Fact]
    public void SaysThePhasesEachNodeMayRunIn()
    {
        PartitionStep partition = Partition();
        ApplyImageStep apply = ApplyImage();
        RunScriptStep inWindows = Script(SequencePhase.Windows);
        RebootStep thenRestart = Reboot();
        RebootStep elseRestart = Reboot();
        GroupStep group = Group(elseRestart);
        IfStep branch = If([inWindows, thenRestart], [group]);
        RebootStep last = Reboot();

        SequenceAnalysis analysis = SequenceValidator.Analyse(Definition(partition, apply, branch, last));

        Assert.Empty(analysis.Problems);
        Assert.Equal(
            [
                $"{partition.Id} WindowsPE",
                $"{apply.Id} WindowsPE",
                $"{branch.Id} WindowsPE, Windows",
                $"{inWindows.Id} Windows",
                $"{thenRestart.Id} Windows",
                $"{group.Id} WindowsPE",
                $"{elseRestart.Id} WindowsPE",
                $"{last.Id} WindowsPE, Windows",
            ],
            analysis.NodePhases.Select(node => $"{node.NodeId} {string.Join(", ", node.Phases)}"));
    }
}
