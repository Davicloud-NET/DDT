// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;
using static DDT.Core.Tests.Sequences.TreeSteps;

namespace DDT.Core.Tests.Sequences;

// A sequence that writes a raw disk image is a Linux sequence. It runs in the Linux boot environment, with shell
// scripts, and has no steps of a Windows installation.
public sealed class SequenceValidatorLinuxTests
{
    private static readonly ShareConnection s_share = new(@"\\files.corp.example\drivers", new AccountReference(Guid.NewGuid(), null));

    [Fact]
    public void AcceptsARawImageWithItsSeedAndShellScriptsAroundThem()
    {
        Assert.Empty(Validate(Shell(), WriteRawImage(), WriteSeed(), Shell()));
        Assert.Empty(Validate(WriteRawImage()));
        Assert.Empty(Validate(Group(Shell()), If([WriteRawImage(), WriteSeed()], [WriteRawImage()]), Pause()));
    }

    [Fact]
    public void RefusesStepsOfAWindowsInstallationBesideARawImage()
    {
        foreach (SequenceStep windows in new SequenceStep[] { Partition(), ApplyImage(), InjectDrivers(), WriteUnattend(), JoinDomain() })
        {
            AssertOnly(Validate(WriteRawImage(), windows), windows, null, "sequence.windowsWithRawImage");
        }

        RunScriptStep inWindows = Script(SequencePhase.Windows);
        AssertOnly(Validate(WriteRawImage(), inWindows), inWindows, "phase", "sequence.windowsWithRawImage");
    }

    // Windows PE ran these scripts until version 4. The one problem says what to do with them, whatever else they hold.
    [Theory]
    [InlineData(ScriptInterpreter.Cmd)]
    [InlineData(ScriptInterpreter.PowerShell)]
    public void TellsAScriptWrittenForWindowsPEWhatToChange(ScriptInterpreter interpreter)
    {
        RunScriptStep old = Script(SequencePhase.WindowsPE) with { Interpreter = interpreter, PackageId = Guid.NewGuid(), RebootExitCodes = [3010] };

        AssertOnly(Validate(old, WriteRawImage(), WriteSeed()), old, "phase", "sequence.windowsScriptInLinux");
        AssertOnly(Validate(WriteRawImage(), Group(old)), old, "phase", "sequence.windowsScriptInLinux");
    }

    [Fact]
    public void RunsOnlyShellScriptsInTheLinuxBootEnvironment()
    {
        RunScriptStep cmd = Shell() with { Interpreter = ScriptInterpreter.Cmd };
        RunScriptStep powerShell = Shell() with { Interpreter = ScriptInterpreter.PowerShell };

        AssertOnly(Validate(WriteRawImage(), cmd), cmd, "interpreter", "sequence.linuxNeedsShell");
        AssertOnly(Validate(powerShell, WriteRawImage()), powerShell, "interpreter", "sequence.linuxNeedsShell");
    }

    [Fact]
    public void RunsNoShellScriptAndNoLinuxPhaseInASequenceThatInstallsWindows()
    {
        RunScriptStep shell = Script(SequencePhase.WindowsPE) with { Interpreter = ScriptInterpreter.Shell };
        RunScriptStep shellInWindows = Script(SequencePhase.Windows) with { Interpreter = ScriptInterpreter.Shell };
        RunScriptStep linux = Shell();
        RunScriptStep linuxCmd = Shell() with { Interpreter = ScriptInterpreter.Cmd };

        AssertOnly(Validate(Partition(), shell), shell, "interpreter", "sequence.shellNeedsLinux");
        AssertOnly(Validate(Partition(), ApplyImage(), shellInWindows), shellInWindows, "interpreter", "sequence.shellNeedsLinux");

        // The phase is the one thing to change, whatever the interpreter.
        AssertOnly(Validate(Partition(), linux), linux, "phase", "sequence.linuxPhaseWithoutRawImage");
        AssertOnly(Validate(Partition(), linuxCmd), linuxCmd, "phase", "sequence.linuxPhaseWithoutRawImage");
    }

    [Fact]
    public void WritesOneRawImageEveryTime()
    {
        WriteRawImageStep again = WriteRawImage();
        WriteRawImageStep conditional = WriteRawImage() with
        {
            Conditions = [new StepCondition(MachineVariableNames.Model, ConditionOperator.Equals, "Latitude 5440")],
        };
        WriteRawImageStep continuing = WriteRawImage() with { ContinueOnError = true };

        AssertOnly(Validate(WriteRawImage(), again), again, null, "sequence.oneRawImage");
        AssertOnly(Validate(conditional), conditional, "conditions", "sequence.cannotSkip");
        AssertOnly(Validate(continuing), continuing, "continueOnError", "sequence.cannotGoOn");
    }

    [Fact]
    public void WritesTheSeedOnceAfterTheRawImage()
    {
        WriteCloudInitSeedStep early = WriteSeed();
        WriteCloudInitSeedStep again = WriteSeed();

        AssertOnly(Validate(early, WriteRawImage()), early, null, "sequence.seedBeforeRawImage");
        AssertOnly(Validate(WriteRawImage(), WriteSeed(), again), again, null, "sequence.oneSeed");
        AssertOnly(Validate(Partition(), early), early, null, "sequence.seedBeforeRawImage");
        Assert.Empty(Validate(WriteRawImage(), WriteSeed() with { ContinueOnError = true }));
    }

    [Fact]
    public void LimitsTheSeedFiles()
    {
        string large = new('x', SequenceValidator.MaxSeedFileBytes + 1);
        WriteCloudInitSeedStep missing = WriteSeed() with { MetaData = null!, UserData = null! };
        WriteCloudInitSeedStep tooLarge = WriteSeed() with { NetworkConfig = large };

        Assert.Equal(["metaData", "userData"], Validate(WriteRawImage(), missing).Select(problem => problem.Field));
        AssertOnly(Validate(WriteRawImage(), tooLarge), tooLarge, "networkConfig", "sequence.seedFileTooLarge");
        Assert.Empty(Validate(WriteRawImage(), WriteSeed() with { MetaData = "", UserData = "", NetworkConfig = null }));
        Assert.Empty(Validate(WriteRawImage(), WriteSeed() with { UserData = large[1..] }));
    }

    // The boot environment keeps the run in memory and has no partition for a package.
    [Fact]
    public void RefusesRestartsAndPackagesInTheLinuxBootEnvironment()
    {
        RebootStep reboot = Reboot();
        RunScriptStep restartAfter = Shell() with { RebootAfter = true };
        RunScriptStep restartCodes = Shell() with { RebootExitCodes = [194] };
        RunScriptStep windowsCode = Shell() with { RebootExitCodes = [3010] };
        RunScriptStep package = Shell() with { PackageId = Guid.NewGuid() };
        WriteRawImageStep restartingWrite = WriteRawImage() with { RebootAfter = true };
        GroupStep restartingGroup = Group(Shell()) with { RebootAfter = true };

        AssertOnly(Validate(WriteRawImage(), reboot), reboot, null, "sequence.restartWithRawImage");
        AssertOnly(Validate(WriteRawImage(), restartAfter), restartAfter, "rebootAfter", "sequence.restartWithRawImage");
        AssertOnly(Validate(WriteRawImage(), restartCodes), restartCodes, "rebootExitCodes", "sequence.restartWithRawImage");
        AssertOnly(Validate(WriteRawImage(), windowsCode), windowsCode, "rebootExitCodes", "sequence.restartWithRawImage");
        AssertOnly(Validate(WriteRawImage(), package), package, "packageId", "sequence.packageWithRawImage");
        AssertOnly(Validate(restartingWrite), restartingWrite, "rebootAfter", "sequence.restartWithRawImage");
        AssertOnly(Validate(WriteRawImage(), restartingGroup), restartingGroup, "rebootAfter", "sequence.restartWithRawImage");
    }

    [Fact]
    public void TakesOnlyExitCodesAShellCanReturn()
    {
        RunScriptStep tooHigh = Shell() with { SuccessExitCodes = [0, 3010] };
        RunScriptStep negative = Shell() with { SuccessExitCodes = [-1] };

        AssertOnly(Validate(WriteRawImage(), tooHigh), tooHigh, "successExitCodes", "sequence.shellExitCodes");
        AssertOnly(Validate(WriteRawImage(), negative), negative, "successExitCodes", "sequence.shellExitCodes");
        Assert.Empty(Validate(WriteRawImage(), Shell() with { SuccessExitCodes = [0, 255] }));

        // cmd and PowerShell keep the whole range.
        Assert.Empty(Validate(Partition(), Script(SequencePhase.WindowsPE) with { SuccessExitCodes = [0, 3010, -1] }));
    }

    [Fact]
    public void ConnectsNoSharesInTheLinuxBootEnvironment()
    {
        RunScriptStep script = Shell() with { Shares = [s_share] };
        WriteCloudInitSeedStep seed = WriteSeed() with { Shares = [s_share] };
        PauseStep inBranch = Pause() with { Shares = [s_share] };

        AssertOnly(Validate(WriteRawImage(), script), script, "shares", "sequence.sharesNotInLinuxBoot");
        AssertOnly(Validate(WriteRawImage(), seed), seed, "shares", "sequence.sharesNotInLinuxBoot");
        AssertOnly(Validate(WriteRawImage(), If([inBranch])), inBranch, "shares", "sequence.sharesNotInLinuxBoot");
        Assert.Empty(Validate(Partition(), Script(SequencePhase.WindowsPE) with { Shares = [s_share] }));
    }

    [Fact]
    public void RunsARunAsScriptOnlyInWindows()
    {
        RunScriptStep script = Shell() with { RunAs = new AccountReference(Guid.NewGuid(), null) };

        AssertOnly(Validate(WriteRawImage(), script), script, "runAs", "sequence.runAsWindowsOnly");
    }

    [Fact]
    public void WarnsOfAPhaseComparedWithAPhaseOfTheOtherKind()
    {
        RunScriptStep when = Shell() with { When = new TestCondition("phase", ConditionOperator.Equals, "windowspe") };
        RunScriptStep legacy = Shell() with { Conditions = [new StepCondition(MachineVariableNames.Phase, ConditionOperator.NotEquals, "Windows")] };
        IfStep test = If([Shell()]) with
        {
            Test = new AllCondition { Parts = [ThinkPad, new TestCondition(MachineVariableNames.Phase, ConditionOperator.In, "LinuxBoot; Windows")] },
        };
        RebootStep windows = Reboot() with { When = new TestCondition(MachineVariableNames.Phase, ConditionOperator.Equals, "LinuxBoot") };

        SequenceAnalysis linux = SequenceValidator.Analyse(Definition(WriteRawImage(), when, legacy, test));

        Assert.Empty(linux.Problems);
        Assert.Equal(
            [
                (when.Id, "when.value", "sequence.phaseValueOtherKindWarning"),
                (legacy.Id, "conditions[0].value", "sequence.phaseValueOtherKindWarning"),
                (test.Id, "test.parts[1].value", "sequence.phaseValueOtherKindWarning"),
            ],
            Said(linux.Warnings));
        Assert.Equal(
            "This sequence writes a raw disk image, so its phase is LinuxBoot and never WindowsPE.",
            linux.Warnings[0].Message);
        AssertOnly(
            SequenceValidator.Analyse(Definition(Partition(), windows)).Warnings,
            windows,
            "when.value",
            "sequence.phaseValueOtherKindWarning");
    }

    [Fact]
    public void LeavesAPhaseComparedWithAPhaseOfItsOwnKindAlone()
    {
        TestCondition boot = new(MachineVariableNames.Phase, ConditionOperator.Equals, "LinuxBoot");
        TestCondition part = new(MachineVariableNames.Phase, ConditionOperator.StartsWith, "Windows");
        TestCondition other = new(MachineVariableNames.Model, ConditionOperator.Equals, "Windows");
        TestCondition windows = new(MachineVariableNames.Phase, ConditionOperator.In, "WindowsPE;Windows");

        Assert.Empty(SequenceValidator.Analyse(Definition(WriteRawImage(), Shell() with { When = new AnyCondition { Parts = [boot, part, other] } })).Warnings);
        Assert.Empty(SequenceValidator.Analyse(Definition(Partition(), Reboot() with { When = windows })).Warnings);
    }

    // What the server reads from a sequence stored before version 4: the document as it was, at the version it needs now.
    [Fact]
    public void JudgesASequenceStoredBeforeVersion4()
    {
        RunScriptStep cmd = Script(SequencePhase.WindowsPE) with { RebootAfter = true, RebootExitCodes = [3010] };
        RunScriptStep powerShell = Script(SequencePhase.WindowsPE) with { Interpreter = ScriptInterpreter.PowerShell };
        WriteCloudInitSeedStep again = WriteSeed();
        PartitionStep windowsStep = Partition();
        RunScriptStep inWindows = Script(SequencePhase.Windows);

        Assert.Empty(Validate(WriteRawImage(), WriteSeed()));
        Assert.Equal(
            [
                (cmd.Id, "rebootAfter", "sequence.restartWithRawImage"),
                (cmd.Id, "phase", "sequence.windowsScriptInLinux"),
                (powerShell.Id, "phase", "sequence.windowsScriptInLinux"),
                (again.Id, null, "sequence.oneSeed"),
                (windowsStep.Id, null, "sequence.windowsWithRawImage"),
                (inWindows.Id, "phase", "sequence.windowsWithRawImage"),
            ],
            Said(Validate(cmd, WriteRawImage(), Group(powerShell), WriteSeed(), again, windowsStep, inWindows)));

        // At the version it was stored with, the version is the problem.
        AssertOnly(SequenceValidator.Validate(new SequenceDefinition(2, [WriteRawImage(), WriteSeed()])), null, "version", "sequence.versionTooLow");
    }

    [Fact]
    public void SaysThatEveryNodeOfALinuxSequenceRunsInTheLinuxBootEnvironment()
    {
        SequenceDefinition definition = Definition(Pause(), Group(Shell(), Pause() with { When = ThinkPad }), WriteRawImage(), WriteSeed());
        SequenceAnalysis analysis = SequenceValidator.Analyse(definition);

        Assert.All(analysis.NodePhases, node => Assert.Equal([SequencePhase.LinuxBoot], node.Phases));
        Assert.Equal(6, analysis.NodePhases.Count);
        Assert.All(Enumerable.Range(0, definition.Steps.Count), index => Assert.Equal(SequencePhase.LinuxBoot, SequencePhases.Of(definition, index)));
    }

    // A script that is refused still shows the phase it names.
    [Fact]
    public void ListsTheWindowsPhaseARefusedScriptNames()
    {
        RunScriptStep old = Script(SequencePhase.WindowsPE);
        RunScriptStep inWindows = Script(SequencePhase.Windows);
        PauseStep after = Pause();
        SequenceAnalysis analysis = SequenceValidator.Analyse(Definition(old, WriteRawImage(), inWindows, after));

        Assert.Equal(
            [
                $"{old.Id} WindowsPE",
                $"{analysis.NodePhases[1].NodeId} LinuxBoot",
                $"{inWindows.Id} Windows",
                $"{after.Id} Windows",
            ],
            analysis.NodePhases.Select(node => $"{node.NodeId} {string.Join(", ", node.Phases)}"));
    }

    [Fact]
    public void StartsARunInTheBootPhaseOfItsKind()
    {
        SequenceState linux = SequenceStates.Start(Guid.NewGuid(), Definition(Shell(), WriteRawImage()));
        SequenceState windows = SequenceStates.Start(Guid.NewGuid(), new SequenceDefinition(1, [Partition()]));
        SequenceState tree = SequenceStates.Start(Guid.NewGuid(), Definition(Group(Partition())));

        Assert.Equal((SequencePhase.LinuxBoot, SequenceState.TreeFormat), (linux.Phase, linux.Format));
        Assert.Equal((SequencePhase.WindowsPE, SequenceState.CurrentFormat), (windows.Phase, windows.Format));
        Assert.Equal((SequencePhase.WindowsPE, SequenceState.TreeFormat), (tree.Phase, tree.Format));
    }
}
