// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Applications;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Disks;
using Xunit;

namespace DDT.Agent.Tests;

// A run that writes a raw disk image and its cloud-init seed in the Linux boot environment, from preflight to the
// restart into the image.
public sealed class LinuxRunTests : IDisposable
{
    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");

    private static readonly WriteRawImageStep s_write = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000e001"),
        Name = "Write the disk",
        ImageId = TestRawImage.ImageId,
    };

    private static readonly WriteCloudInitSeedStep s_seed = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000e002"),
        Name = "Seed",
        MetaData = "instance-id: \"{{SmbiosUuid}}\"\nlocal-hostname: \"{{ComputerName}}\"\n",
        UserData = "#cloud-config\n",
    };

    private static readonly RunScriptStep s_script = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000e003"),
        Name = "Before the disk",
        Phase = SequencePhase.LinuxBoot,
        Interpreter = ScriptInterpreter.Shell,
        Script = "echo \"$DDT_PHASE\"",
        RebootExitCodes = [],
    };

    private readonly FakeDeploymentTools _tools = new();
    private readonly RecordingToolRunner _toolRunner = new();
    private readonly MemoryRawDisks _disks = new();
    private readonly TestRawImage _image = new();
    private readonly Dictionary<string, string> _scriptEnvironment = new(StringComparer.Ordinal);

    public void Dispose() => _tools.Dispose();

    private static MachineIdentity Identity(bool? secureBootEnabled) =>
        new DryRunMachineIdentityReader(1).Read() with { SecureBootEnabled = secureBootEnabled };

    private AgentRun Run(ImageBootCapability capability = ImageBootCapability.SecureBootOk, bool allowed = false, params SequenceStep[] steps) =>
        new(
            TestRuns.RunId,
            DeploymentState.Assigned,
            "Install Linux",
            new SequenceDefinition(SequenceDefinition.CurrentVersion, steps.Length > 0 ? steps : [s_write, s_seed]),
            [_image.RunImage(capability)],
            [],
            null,
            "LINUX-01",
            allowed);

    // The runner of an agent that started in environment, the Linux boot environment unless a test says otherwise.
    private async Task<(RunResult Result, ScriptedAgentServer Server, string Console)> RunAsync(
        AgentRun run,
        bool? secureBootEnabled = true,
        ScriptedAgentServer? server = null,
        AgentEnvironment environment = AgentEnvironment.LinuxBoot)
    {
        server ??= _image.Serve(new ScriptedAgentServer());
        StringWriter console = new();
        SequenceRunner runner = Runner(server, console, environment);

        RunResult result = await runner.RunAsync(new RunRequest(s_machineId, run, null, null, new DeploymentTokens("session-0", "resume-0"), Identity(secureBootEnabled)), server.Stop.Token);

        return (result, server, console.ToString());
    }

    private SequenceRunner Runner(ScriptedAgentServer server, StringWriter console, AgentEnvironment environment = AgentEnvironment.LinuxBoot)
    {
        ImmediateTimeProvider time = new();
        TestRunnerOptions options = new() { RawDisks = _disks, Environment = environment, ToolRunner = _toolRunner, ScriptEnvironment = _scriptEnvironment };

        return TestAgents.Runner(server, _tools, new AgentLog(time, console), time, options);
    }

    [Fact]
    public async Task WritesTheImageAndTheSeedThenStartsTheImageFirst()
    {
        (RunResult result, ScriptedAgentServer server, _) = await RunAsync(Run());

        Assert.Equal(new RunResult(RunOutcome.Finished), result);
        Assert.Equal(
            ["list", "clean 0", $"firmware noble-test {FirmwareBootEntry.FallbackLoaderPath} on partition {TestRawImage.EspNumber}", "reboot"],
            _tools.Calls);
        Assert.Equal(RestartInto.InstalledSystem, TestAgents.RestartMarker(_tools, new AgentLog(new ImmediateTimeProvider(), TextWriter.Null)).Due);
        Assert.Equal((DeploymentState.Done, RunActivity.Finishing), (server.RunReports[^1].State, server.RunReports[^1].Activity));
        Assert.Equal([StepState.Done, StepState.Done], server.RunReports[^1].Steps.Select(step => step.State));
        Assert.All(server.RunReports, report => Assert.Equal(SequencePhase.LinuxBoot, report.Phase));

        // Nothing of the run is kept on the disk it wrote, because the image is the disk.
        MemoryRawDisk disk = _disks.Disks[0];
        GptLayout layout = GptLayout.Read(disk.ReadAt(0, RawDiskWriter.HeadBytes));
        Assert.Equal(["EFI", "root", CloudInitSeed.Label], layout.Partitions.Select(partition => partition.Name));
    }

    // The script runs through the shell from the agent's own directory, since the boot environment has no partition.
    [Fact]
    public async Task RunsAShellScriptInTheLinuxBootEnvironmentBeforeTheImage()
    {
        (RunResult result, ScriptedAgentServer server, _) = await RunAsync(Run(steps: [s_script, s_write, s_seed]));

        string file = Path.Combine(_tools.Root, "X", "scripts", $"{s_script.Id:D}.sh");
        Assert.Equal(new RunResult(RunOutcome.Finished), result);
        Assert.Equal([RecordingToolRunner.CommandLine(ShellScriptLauncher.DefaultShell, file)], _toolRunner.Calls);
        Assert.Equal("LinuxBoot", _toolRunner.Options[0].Environment?["DDT_PHASE"]);
        Assert.Equal([StepState.Done, StepState.Done, StepState.Done], server.RunReports[^1].Steps.Select(step => step.State));
    }

    // The other kind's boot environment refuses the run with a sentence that names the right one, whatever else is
    // wrong with it there.
    [Theory]
    [InlineData(ImageBootCapability.SecureBootOk)]
    [InlineData(ImageBootCapability.NotSigned)]
    public async Task WindowsPERefusesTheRunBeforeTheDiskIsTouched(ImageBootCapability capability)
    {
        (RunResult result, ScriptedAgentServer server, string console) = await RunAsync(Run(capability), environment: AgentEnvironment.WindowsPE);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Empty(_tools.Calls);
        Assert.Empty(_disks.Disks);
        Assert.Equal(
            "This sequence writes a raw disk image, which only the Linux boot environment does, and this machine started Windows PE. " +
            "Nothing was erased.",
            server.RunReports[^1].Error);
        Assert.Equal((DeploymentState.Failed, SequencePhase.LinuxBoot), (server.RunReports[^1].State, server.RunReports[^1].Phase));
        Assert.Contains("The run cannot start: This sequence writes a raw disk image", console, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLinuxBootEnvironmentRefusesASequenceThatInstallsWindows()
    {
        AgentRun windows = Run(steps: [TestRuns.Partition]) with { Images = [] };

        (RunResult result, ScriptedAgentServer server, _) = await RunAsync(windows);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Empty(_tools.Calls);
        Assert.Equal(BootEnvironmentCheck.Message(SequenceKind.Windows, AgentEnvironment.LinuxBoot), server.RunReports[^1].Error);
    }

    // The server hands an agent nothing newer than the agent says it runs. Should it ever, the run ends before
    // anything is erased, with what to do about it.
    [Fact]
    public async Task RefusesASequenceOfANewerVersionThanItRunsBeforeTheDiskIsTouched()
    {
        LaterStep later = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000e009"), Name = "From a newer DDT" };

        (RunResult result, ScriptedAgentServer server, _) = await RunAsync(Run(steps: [s_write, s_seed, later]));

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Empty(_tools.Calls);
        Assert.Empty(_toolRunner.Calls);
        Assert.Equal(
            $"Install Linux has steps of version {SupportedSequences.Version + 1}, and this agent runs sequences up to version " +
            $"{SupportedSequences.Version}. Start the machine from the network again, so it gets the server's agent. Nothing was erased.",
            server.RunReports[^1].Error);
    }

    // A kind of step from a version after this agent's.
    private sealed record LaterStep : SequenceStep
    {
        public override SequencePhase? RequiredPhase => null;

        public override int MinimumVersion => SupportedSequences.Version + 1;
    }

    // A run assigned before the sequence was rewritten for the shell, and one whose script asks for what the boot
    // environment cannot do. The validator's sentence says what to change, and nothing is erased.
    [Fact]
    public async Task RefusesWhatTheValidatorRefusesBeforeTheDiskIsTouched()
    {
        RunScriptStep forWindowsPE = s_script with { Phase = SequencePhase.WindowsPE, Interpreter = ScriptInterpreter.Cmd };
        RunScriptStep restarting = s_script with { RebootExitCodes = [194] };
        RunScriptStep withPackage = s_script with { PackageId = Guid.NewGuid() };

        (RunResult old, ScriptedAgentServer oldServer, _) = await RunAsync(Run(steps: [forWindowsPE, s_write, s_seed]));
        (_, ScriptedAgentServer restartServer, _) = await RunAsync(Run(steps: [restarting, s_write]));
        (_, ScriptedAgentServer packageServer, _) = await RunAsync(Run(steps: [withPackage, s_write]));

        Assert.Equal(RunOutcome.Failed, old.Outcome);
        Assert.Empty(_tools.Calls);
        Assert.Empty(_toolRunner.Calls);
        Assert.Equal(
            "Install Linux cannot run: This script was written for Windows PE, which no longer runs Linux sequences. Rewrite it for " +
            "the shell and let it run in the Linux boot environment or in the installed Linux. Correct the sequence and assign it again.",
            oldServer.RunReports[^1].Error);
        Assert.StartsWith("Install Linux cannot run: The Linux boot environment keeps the run's state in memory", restartServer.RunReports[^1].Error, StringComparison.Ordinal);
        Assert.StartsWith("Install Linux cannot run: The Linux boot environment has no partition to unpack a package on", packageServer.RunReports[^1].Error, StringComparison.Ordinal);
    }

    // The agent says version 7, so the server hands it such a run, and the boot environment asks for the hand-over at
    // the first step in the installed Linux.
    [Fact]
    public async Task TakesARunWithStepsInTheInstalledLinuxAndHandsItOverAfterTheSeed()
    {
        RunScriptStep installed = s_script with { Id = Guid.NewGuid(), Phase = SequencePhase.Linux };

        (RunResult result, ScriptedAgentServer server, _) = await RunAsync(Run(steps: [s_write, s_seed, installed]));

        Assert.Equal(7, SupportedSequences.Version);
        Assert.Equal(RunOutcome.Restarting, result.Outcome);
        Assert.Equal(["list", "clean 0", "hand over", "reboot"], _tools.Calls);
        Assert.Empty(_toolRunner.Calls);
        Assert.Equal((DeploymentState.Running, RunActivity.Restarting), (server.RunReports[^1].State, server.RunReports[^1].Activity));
    }

    // The agent for Linux says version 7 too, but installing applications belongs to a Windows sequence.
    [Fact]
    public async Task RefusesASequenceThatInstallsApplicationsBeforeTheDiskIsTouched()
    {
        InstallApplicationsStep install = new()
        {
            Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000e00a"),
            Name = "Install applications",
            Applications = [new ApplicationReference(Guid.Parse("0193a4b2-0000-7000-8000-00000000e00b"), ApplicationReferenceKind.Application)],
        };

        (RunResult result, ScriptedAgentServer server, _) = await RunAsync(Run(steps: [s_write, s_seed, install]));

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Empty(_tools.Calls);
        Assert.Empty(_toolRunner.Calls);
        Assert.Equal(
            "Install Linux cannot run: A sequence either installs Windows or writes a raw disk image. This step belongs to installing Windows. " +
            "Correct the sequence and assign it again.",
            server.RunReports[^1].Error);
    }

    [Fact]
    public async Task RefusesTheImageBeforeTheDiskIsTouchedWhereSecureBootIsOn()
    {
        (RunResult result, ScriptedAgentServer server, string console) = await RunAsync(Run(ImageBootCapability.NotSigned));

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Empty(_tools.Calls);
        Assert.Empty(_disks.Disks);
        Assert.Equal(DeploymentState.Failed, server.RunReports[^1].State);
        Assert.StartsWith("noble-test is not signed for Secure Boot, and this machine has Secure Boot on", server.RunReports[^1].Error, StringComparison.Ordinal);
        Assert.Contains("The run cannot start: noble-test is not signed for Secure Boot", console, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WarnsAndWritesAnImageTheRunWasAllowedToWrite()
    {
        (RunResult result, _, string console) = await RunAsync(Run(ImageBootCapability.NotSigned, allowed: true));

        Assert.Equal(RunOutcome.Finished, result.Outcome);
        Assert.Contains("The run was allowed to write it", console, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesADiskTooSmallForTheImageAndTheSeed()
    {
        _tools.Disks[0] = FakeDeploymentTools.Disk(0, sizeBytes: _image.Disk.Length + (CloudInitSeed.DiskBytes / 2));

        (RunResult result, ScriptedAgentServer server, _) = await RunAsync(Run());

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal(["list"], _tools.Calls);
        Assert.StartsWith("Disk 0 holds 41 MB, but Install Linux needs 74 MB: 8 MB for the disk image and 66 MB for the cloud-init seed.", server.RunReports[^1].Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesADiskWithSectorsOfFourKilobytesBeforeCleaningIt()
    {
        _disks.SectorSize = 4096;

        (RunResult result, ScriptedAgentServer server, _) = await RunAsync(Run());

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal(["list"], _tools.Calls);
        Assert.Equal($"Disk 0 {WriteRawImageStepRunner.FourKilobyteSectorsMessage}", server.RunReports[^1].Error);
    }

    // Checked once the run has its values, which may name the machine, and before the first step.
    [Fact]
    public async Task RefusesASeedWithAValueTheMachineLacksBeforeTheDiskIsTouched()
    {
        WriteCloudInitSeedStep unnamed = s_seed with { MetaData = "hostname: \"{{ComputerName}}\"" };
        AgentRun run = Run(steps: [s_write, unnamed]) with { ComputerName = null };

        (RunResult result, ScriptedAgentServer server, _) = await RunAsync(run);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal(["list"], _tools.Calls);
        Assert.All(_disks.Disks.Values, disk => Assert.Empty(disk.Writes));
        Assert.Equal((DeploymentState.Failed, 0), (server.RunReports[^1].State, server.RunReports[^1].Steps.Count));
        Assert.StartsWith("The machine has no value for {{ComputerName}}. Assign the sequence with a computer name", server.RunReports[^1].Error, StringComparison.Ordinal);
    }

    // A run assigned without a name, which a rule's pattern then gives it. The values from the report that started the
    // run name the machine in its seed.
    [Fact]
    public async Task NamesTheMachineInTheSeedAsTheRunsValuesDo()
    {
        AgentRun run = Run() with { ComputerName = null };
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());
        server.AnswerRunReports = (_, token) =>
            new AgentRunReportResult(token, "resume", "run-token-1", Values: new Dictionary<string, string> { [MachineVariableNames.ComputerName] = "SEA-00042" });

        (RunResult result, _, _) = await RunAsync(run, server: server);

        Assert.Equal(new RunResult(RunOutcome.Finished), result);
        MemoryRawDisk disk = _disks.Disks[0];
        GptPartition seed = GptLayout.Read(disk.ReadAt(0, RawDiskWriter.HeadBytes)).Partitions.Single(partition => partition.Name == CloudInitSeed.Label);
        byte[] volume = disk.ReadAt(seed.FirstLba * GptLayout.SectorSize, (int)(seed.Sectors * GptLayout.SectorSize));
        FatVolume fat = FatVolume.Open(new MemoryStream(volume), 0, volume.Length);
        Assert.Contains("local-hostname: \"SEA-00042\"", Encoding.UTF8.GetString(fat.ReadFile(fat.Find(CloudInitSeed.MetaData)!, 64 * 1024)), StringComparison.Ordinal);
    }

    // Only once the image is written does the disk hold what a script could change, and DDT's own variables stay.
    [Fact]
    public async Task TellsAScriptAfterTheImageWhichDiskItWasWrittenTo()
    {
        _tools.Disks[0] = FakeDeploymentTools.Disk(0) with { Device = "/dev/nvme0n1" };
        _scriptEnvironment["PATH"] = "/usr/bin";
        _scriptEnvironment["DDT_PHASE"] = "theirs";
        RunScriptStep after = s_script with { Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000e004"), Name = "After the disk" };

        (RunResult result, _, _) = await RunAsync(Run(steps: [s_script, s_write, s_seed, after]));

        Assert.Equal(new RunResult(RunOutcome.Finished), result);
        Assert.False(_toolRunner.Options[0].Environment?.ContainsKey(RunScriptStepRunner.Disk));
        Assert.Equal("/dev/nvme0n1", _toolRunner.Options[1].Environment?[RunScriptStepRunner.Disk]);
        Assert.All(_toolRunner.Options, options => Assert.Equal(("/usr/bin", "LinuxBoot"), (options.Environment?["PATH"], options.Environment?["DDT_PHASE"])));
    }

    [Fact]
    public async Task WithoutADiskItSaysWhatTheLinuxBootEnvironmentMayLack()
    {
        _tools.Disks.Clear();

        (RunResult result, ScriptedAgentServer server, _) = await RunAsync(Run());

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal(EnvironmentTexts.LinuxNoDisk, server.RunReports[^1].Error);
    }

    [Fact]
    public async Task SaysThatTheFirmwareDoesNotTellWhetherSecureBootIsOn()
    {
        (RunResult result, _, string console) = await RunAsync(Run(ImageBootCapability.NotSigned), secureBootEnabled: null);

        Assert.Equal(RunOutcome.Finished, result.Outcome);
        Assert.Contains(
            "noble-test is not signed for Secure Boot, and this machine's firmware does not say whether Secure Boot is on. If it is, the machine will not start the image.",
            console,
            StringComparison.Ordinal);
    }

    // The agent was started again at the machine before the restart it owed had happened.
    [Theory]
    [InlineData(RestartInto.InstalledSystem, "The machine was to start the installed Linux but has not restarted since. Restarting it now.")]
    [InlineData(RestartInto.BootEnvironment, "The machine was to restart into the Linux boot environment for the run but has not restarted since. Restarting it now.")]
    public async Task RestartsFirstWhenARestartIsStillDueAndSaysSoInItsOwnWords(RestartInto into, string sentence)
    {
        StringWriter console = new();
        TestAgents.RestartMarker(_tools, new AgentLog(new ImmediateTimeProvider(), TextWriter.Null)).Set(into);

        RunOutcome? outcome = await Runner(new ScriptedAgentServer(), console).RestartIfDueAsync(TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.Restarting, outcome);
        Assert.StartsWith("reboot", Assert.Single(_tools.Calls), StringComparison.Ordinal);
        Assert.Contains(sentence, console.ToString(), StringComparison.Ordinal);
    }

    // A seed step with conditions is left to its step, which fails after the image was written.
    [Fact]
    public async Task AFailedSeedLeavesTheBootOrderAndOwesNoRestart()
    {
        WriteCloudInitSeedStep unnamed = s_seed with
        {
            MetaData = "hostname: \"{{ComputerName}}\"",
            Conditions = [new StepCondition(MachineVariableNames.Phase, ConditionOperator.Equals, "LinuxBoot")],
        };
        AgentRun run = Run(steps: [s_write, unnamed]) with { ComputerName = null };

        (RunResult result, ScriptedAgentServer server, _) = await RunAsync(run);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal(["list", "clean 0"], _tools.Calls);
        Assert.Null(TestAgents.RestartMarker(_tools, new AgentLog(new ImmediateTimeProvider(), TextWriter.Null)).Due);
        Assert.Equal([StepState.Done, StepState.Failed], server.RunReports[^1].Steps.Select(step => step.State));
    }
}
