// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;
using static DDT.Core.Tests.Sequences.TreeSteps;

namespace DDT.Core.Tests.Sequences;

// A Linux sequence goes on in the installed Linux, where its seed carried the agent. There a script can restart, have
// a package and connect shares, none of which the Linux boot environment can.
public sealed class SequenceValidatorInstalledLinuxTests
{
    private static readonly ShareConnection s_share = new(@"\\files.corp.example\tools", new AccountReference(Guid.NewGuid(), null));

    private static readonly StepCondition s_onOneModel = new(MachineVariableNames.Model, ConditionOperator.Equals, "Latitude 5440");

    private static IReadOnlyList<SequencePhase> PhasesOf(SequenceAnalysis analysis, SequenceStep node) =>
        Assert.Single(analysis.NodePhases, phases => phases.NodeId == node.Id).Phases;

    [Fact]
    public void AcceptsStepsInTheInstalledLinuxAfterTheSeed()
    {
        SequenceDefinition everything = Definition(
            Shell(),
            WriteRawImage(),
            WriteSeed(),
            InLinux(),
            Pause(),
            SetVariable("Site", "{{Model}}"),
            Reboot(),
            Group(InLinux(), Repeat(InLinux(), Pause()))) with
        {
            Variables = [new VariableDeclaration { Name = "Site", SetBySteps = true }],
        };

        Assert.Empty(Validate(WriteRawImage(), WriteSeed(), InLinux()));
        Assert.Empty(Validate(WriteRawImage(), If([WriteSeed()], [WriteSeed()]), InLinux()));

        SequenceAnalysis analysis = SequenceValidator.Analyse(everything);
        Assert.Empty(analysis.Problems);
        Assert.Empty(analysis.Warnings);
    }

    [Fact]
    public void NeedsASeedThatIsWrittenEveryTimeBeforeAStepInTheInstalledLinux()
    {
        RunScriptStep none = InLinux();
        RunScriptStep conditional = InLinux();
        RunScriptStep when = InLinux();
        RunScriptStep goesOn = InLinux();
        RunScriptStep early = InLinux();

        IReadOnlyList<SequenceProblem> problems = Validate(WriteRawImage(), none);

        AssertOnly(problems, none, "phase", "sequence.linuxNeedsSeed");
        Assert.Equal(
            "A step in the installed Linux needs an earlier step that writes the cloud-init seed, without conditions and without going " +
            "on when it fails. The seed takes DDT's agent into the installed Linux.",
            problems[0].Message);
        AssertOnly(Validate(WriteRawImage(), WriteSeed() with { Conditions = [s_onOneModel] }, conditional), conditional, "phase", "sequence.linuxNeedsSeed");
        AssertOnly(Validate(WriteRawImage(), WriteSeed() with { When = ThinkPad }, when), when, "phase", "sequence.linuxNeedsSeed");
        AssertOnly(Validate(WriteRawImage(), WriteSeed() with { ContinueOnError = true }, goesOn), goesOn, "phase", "sequence.linuxNeedsSeed");
        Assert.Equal("sequence.linuxNeedsSeed", Validate(WriteRawImage(), early, WriteSeed())[0].Code);
    }

    [Fact]
    public void NeedsTheSeedOnEveryPathToAStepInTheInstalledLinux()
    {
        RunScriptStep oneBranch = InLinux();
        RunScriptStep skippedGroup = InLinux();
        RunScriptStep failedGroup = InLinux();

        IReadOnlyList<SequenceProblem> problems = Validate(WriteRawImage(), If([WriteSeed()]), oneBranch);

        AssertOnly(problems, oneBranch, "phase", "sequence.linuxNeedsSeedOnEveryPath");
        Assert.Equal(
            "A step in the installed Linux needs the cloud-init seed written before it on every path through the sequence, and on some " +
            "path no step writes it.",
            problems[0].Message);
        AssertOnly(
            Validate(WriteRawImage(), Group(WriteSeed()) with { When = ThinkPad }, skippedGroup),
            skippedGroup,
            "phase",
            "sequence.linuxNeedsSeedOnEveryPath");
        AssertOnly(
            Validate(WriteRawImage(), Group(Shell(), WriteSeed()) with { ContinueOnError = true }, failedGroup),
            failedGroup,
            "phase",
            "sequence.linuxNeedsSeedOnEveryPath");

        // Only a step that asks for the installed Linux needs the seed. What follows it is there already.
        Assert.Empty(Validate(WriteRawImage(), WriteSeed(), If([InLinux()], [InLinux()]), Pause(), Reboot()));
    }

    [Fact]
    public void RefusesAStepOfTheLinuxBootEnvironmentAfterTheInstalledLinux()
    {
        RunScriptStep late = Shell();
        RunScriptStep afterABranch = Shell();
        WriteCloudInitSeedStep lateSeed = WriteSeed();

        IReadOnlyList<SequenceProblem> problems = Validate(WriteRawImage(), WriteSeed(), InLinux(), late);

        AssertOnly(problems, late, "phase", "sequence.linuxBootAfterLinux");
        Assert.Equal(
            "Steps in the Linux boot environment come first, and an earlier step already runs in the installed Linux.",
            problems[0].Message);
        AssertOnly(Validate(WriteRawImage(), WriteSeed(), If([InLinux()]), afterABranch), afterABranch, "phase", "sequence.linuxBootAfterLinux");
        Assert.Contains((lateSeed.Id, null, "sequence.linuxBootAfterLinux"), Said(Validate(WriteRawImage(), WriteSeed(), InLinux(), lateSeed)));
        Assert.Empty(Validate(Shell(), WriteRawImage(), Shell(), WriteSeed(), Shell(), InLinux(), InLinux()));
    }

    [Fact]
    public void KeepsARepeatInOnePhaseOfALinuxSequence()
    {
        RunScriptStep changing = InLinux();

        IReadOnlyList<SequenceProblem> problems = Validate(WriteRawImage(), WriteSeed(), Repeat(changing));

        AssertOnly(problems, changing, "phase", "sequence.phaseChangeInRepeat");
        Assert.Equal(
            "A run cannot change between the Linux boot environment and the installed Linux inside a repeat. Move this step, or the " +
            "steps before it, out of the repeat.",
            problems[0].Message);
        Assert.Empty(Validate(WriteRawImage(), WriteSeed(), InLinux(), Repeat(InLinux(), Pause())));
        Assert.Empty(Validate(WriteRawImage(), Repeat(Shell()), WriteSeed(), InLinux()));

        // A sequence that installs Windows keeps its own words.
        RunScriptStep inWindows = Script(SequencePhase.Windows);
        IReadOnlyList<SequenceProblem> windows = Validate(Partition(), ApplyImage(), Repeat(inWindows));
        AssertOnly(windows, inWindows, "phase", "sequence.phaseChangeInRepeat");
        Assert.StartsWith("A run cannot change between Windows PE and Windows inside a repeat.", windows[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RestartsInTheInstalledLinuxAndNotInTheLinuxBootEnvironment()
    {
        RebootStep mayBeInBoot = Reboot();
        RebootStep inBoot = Reboot();

        Assert.Empty(Validate(
            WriteRawImage(),
            WriteSeed(),
            InLinux() with { RebootAfter = true },
            Reboot(),
            InLinux() with { RebootExitCodes = [194] },
            Group(InLinux()) with { RebootAfter = true }));

        IReadOnlyList<SequenceProblem> problems = Validate(WriteRawImage(), WriteSeed(), inBoot, InLinux());

        AssertOnly(problems, inBoot, null, "sequence.restartWithRawImage");
        Assert.Equal(
            "The Linux boot environment keeps the run's state in memory, so it cannot restart during a run. The installed Linux can.",
            problems[0].Message);

        // On the branch that has no step in the installed Linux, the restart is one of the boot environment.
        AssertOnly(Validate(WriteRawImage(), WriteSeed(), If([InLinux()]), mayBeInBoot), mayBeInBoot, null, "sequence.restartWithRawImage");
    }

    [Fact]
    public void TakesOnlyRestartCodesAShellCanReturn()
    {
        RunScriptStep windowsCode = InLinux() with { RebootExitCodes = [3010] };
        RunScriptStep both = InLinux() with { SuccessExitCodes = [0, 194], RebootExitCodes = [194] };

        IReadOnlyList<SequenceProblem> problems = Validate(WriteRawImage(), WriteSeed(), windowsCode);

        AssertOnly(problems, windowsCode, "rebootExitCodes", "sequence.shellRebootExitCodes");
        Assert.Equal(
            "A shell script ends with an exit code from 0 to 255. For a restart use 194, which is what a shell makes of exit 3010.",
            problems[0].Message);
        AssertOnly(Validate(WriteRawImage(), WriteSeed(), both), both, "rebootExitCodes", "sequence.exitCodeMeansBoth");
        Assert.Empty(Validate(WriteRawImage(), WriteSeed(), InLinux() with { RebootExitCodes = [194, 255] }));
        Assert.Empty(Validate(WriteRawImage(), WriteSeed(), InLinux() with { RebootExitCodes = [] }));
    }

    [Fact]
    public void GivesAScriptAPackageInTheInstalledLinuxAndNotInTheLinuxBootEnvironment()
    {
        RunScriptStep inBoot = Shell() with { PackageId = Guid.NewGuid() };

        Assert.Empty(Validate(WriteRawImage(), WriteSeed(), InLinux() with { PackageId = Guid.NewGuid() }));

        IReadOnlyList<SequenceProblem> problems = Validate(WriteRawImage(), WriteSeed(), inBoot, InLinux());

        AssertOnly(problems, inBoot, "packageId", "sequence.packageWithRawImage");
        Assert.Equal(
            "The Linux boot environment has no partition to unpack a package on, so a script there cannot have one. A script in the " +
            "installed Linux can.",
            problems[0].Message);
    }

    [Fact]
    public void ConnectsSharesInTheInstalledLinuxAndNotInTheLinuxBootEnvironment()
    {
        PauseStep mayBeInBoot = Pause() with { Shares = [s_share] };
        RunScriptStep inBoot = Shell() with { Shares = [s_share] };

        Assert.Empty(Validate(WriteRawImage(), WriteSeed(), InLinux() with { Shares = [s_share] }, Pause() with { Shares = [s_share] }));

        IReadOnlyList<SequenceProblem> problems = Validate(WriteRawImage(), WriteSeed(), inBoot, InLinux());

        AssertOnly(problems, inBoot, "shares", "sequence.sharesNotInLinuxBoot");
        Assert.Equal("The Linux boot environment cannot connect shares. Only a step in the installed Linux can have them.", problems[0].Message);
        AssertOnly(Validate(WriteRawImage(), WriteSeed(), If([InLinux()]), mayBeInBoot), mayBeInBoot, "shares", "sequence.sharesNotInLinuxBoot");
    }

    // The run's state is on the disk there, so a restart while it is paused ends nothing.
    [Fact]
    public void WarnsOfAPauseInTheLinuxBootEnvironmentAndNotInTheInstalledLinux()
    {
        PauseStep inBoot = Pause();
        PauseStep installed = Pause();

        SequenceAnalysis analysis = SequenceValidator.Analyse(Definition(WriteRawImage(), WriteSeed(), inBoot, InLinux(), installed));

        Assert.Empty(analysis.Problems);
        AssertOnly(analysis.Warnings, inBoot, null, "sequence.pauseBeforePartitionWarning");
    }

    [Fact]
    public void RunsOnlyShellScriptsAsRootAndJoinsNoDomainInTheInstalledLinux()
    {
        RunScriptStep cmd = InLinux() with { Interpreter = ScriptInterpreter.Cmd };
        RunScriptStep runAs = InLinux() with { RunAs = new AccountReference(Guid.NewGuid(), null) };
        JoinDomainStep join = JoinDomain();

        IReadOnlyList<SequenceProblem> interpreter = Validate(WriteRawImage(), WriteSeed(), cmd);
        IReadOnlyList<SequenceProblem> account = Validate(WriteRawImage(), WriteSeed(), runAs);

        AssertOnly(interpreter, cmd, "interpreter", "sequence.linuxNeedsShell");
        Assert.Equal(
            "The Linux boot environment and the installed Linux run only shell scripts. Choose the shell and rewrite the script for it.",
            interpreter[0].Message);
        AssertOnly(account, runAs, "runAs", "sequence.runAsWindowsOnly");
        Assert.Equal("A script runs as an account only in Windows. In the installed Linux it runs as root.", account[0].Message);
        AssertOnly(Validate(WriteRawImage(), WriteSeed(), InLinux(), join), join, null, "sequence.windowsWithRawImage");
    }

    [Fact]
    public void TellsAScriptWrittenForWindowsPEThatItCanRunInTheInstalledLinuxToo()
    {
        RunScriptStep old = Script(SequencePhase.WindowsPE);

        Assert.Equal(
            "This script was written for Windows PE, which no longer runs Linux sequences. Rewrite it for the shell and let it run in " +
            "the Linux boot environment or in the installed Linux.",
            Assert.Single(Validate(old, WriteRawImage(), WriteSeed())).Message);
    }

    [Fact]
    public void WarnsOfAPhaseComparedWithTheInstalledLinuxOnlyInASequenceThatInstallsWindows()
    {
        TestCondition inLinux = new(MachineVariableNames.Phase, ConditionOperator.Equals, "linux");
        RebootStep windows = Reboot() with { When = inLinux };

        Assert.Empty(SequenceValidator.Analyse(Definition(WriteRawImage(), WriteSeed(), InLinux(), Pause() with { When = inLinux })).Warnings);

        SequenceAnalysis analysis = SequenceValidator.Analyse(Definition(Partition(), windows));

        AssertOnly(analysis.Warnings, windows, "when.value", "sequence.phaseValueOtherKindWarning");
        Assert.Equal("This sequence installs Windows, so its phase is WindowsPE or Windows and never Linux.", analysis.Warnings[0].Message);
    }

    [Fact]
    public void SaysWhichNodesRunInTheInstalledLinux()
    {
        RunScriptStep boot = Shell();
        RunScriptStep first = InLinux();
        PauseStep after = Pause();
        GroupStep group = Group(Pause());
        IfStep maybe = If([InLinux()]);
        PauseStep either = Pause();

        SequenceDefinition straight = Definition(boot, WriteRawImage(), WriteSeed(), first, after, group);
        SequenceAnalysis analysis = SequenceValidator.Analyse(straight);
        SequenceAnalysis branched = SequenceValidator.Analyse(Definition(WriteRawImage(), WriteSeed(), maybe, either));

        Assert.Equal([SequencePhase.LinuxBoot], PhasesOf(analysis, boot));
        Assert.Equal([SequencePhase.Linux], PhasesOf(analysis, first));
        Assert.Equal([SequencePhase.Linux], PhasesOf(analysis, after));
        Assert.Equal([SequencePhase.Linux], PhasesOf(analysis, group));
        Assert.Equal([SequencePhase.Linux], PhasesOf(analysis, group.Steps[0]));
        Assert.Equal([SequencePhase.LinuxBoot, SequencePhase.Linux], PhasesOf(branched, maybe));
        Assert.Equal([SequencePhase.LinuxBoot, SequencePhase.Linux], PhasesOf(branched, either));
        Assert.Equal(
            [SequencePhase.LinuxBoot, SequencePhase.LinuxBoot, SequencePhase.LinuxBoot, SequencePhase.Linux, SequencePhase.Linux, SequencePhase.Linux],
            Enumerable.Range(0, straight.Steps.Count).Select(index => SequencePhases.Of(straight, index)));
    }

    // The engine stops before the first step that asks for the installed Linux, as it does before Windows.
    [Fact]
    public async Task StartsInTheLinuxBootEnvironmentAndAsksForTheHandOverAtTheFirstStepInLinux()
    {
        WriteRawImageStep write = WriteRawImage();
        WriteCloudInitSeedStep seed = WriteSeed();
        RunScriptStep installed = InLinux();
        PauseStep after = Pause();
        SequenceState start = SequenceStates.Start(Guid.NewGuid(), Definition(write, seed, installed, after));
        MachineVariables machine = new("QEMU", "Standard PC", "1", "4c4c4544-0042-3510-8052-b4c04f4d3232", ["525400123456"], "linux-01", start.Phase);
        ScriptedStepRunner inBoot = new();
        BlobStore store = new();

        SequenceRunResult handOver = await new SequenceEngine(inBoot, store, new StepPercents())
            .RunAsync(start, machine, TestContext.Current.CancellationToken);

        Assert.Equal((SequencePhase.LinuxBoot, SequenceState.TreeFormat), (start.Phase, start.Format));
        Assert.Equal(SequenceOutcome.PhaseChangeRequired, handOver.Outcome);
        Assert.Equal([write.Id, seed.Id], inBoot.Ran);

        ScriptedStepRunner inLinux = new();
        SequenceRunResult finished = await new SequenceEngine(inLinux, new BlobStore(), new StepPercents())
            .RunAsync(BlobStore.Load(store.Latest) with { Phase = SequencePhase.Linux }, machine, TestContext.Current.CancellationToken);

        Assert.Equal(SequenceOutcome.Completed, finished.Outcome);
        Assert.Equal([installed.Id, after.Id], inLinux.Ran);
        Assert.All(inLinux.Runs, run => Assert.Equal(SequencePhase.Linux, run.Context.Phase));
        Assert.All(inLinux.Runs, run => Assert.Equal(["Linux"], run.Context.Machine.Values(MachineVariableNames.Phase)));
    }
}
