// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Disks;
using Xunit;

namespace DDT.Agent.Tests;

// A run that writes a raw disk image and its cloud-init seed, from preflight to the restart into the image.
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

    private readonly FakeDeploymentTools _tools = new();
    private readonly MemoryRawDisks _disks = new();
    private readonly TestRawImage _image = new();

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

    private async Task<(RunResult Result, ScriptedAgentServer Server, string Console)> RunAsync(
        AgentRun run,
        bool? secureBootEnabled = true,
        ScriptedAgentServer? server = null)
    {
        server ??= _image.Serve(new ScriptedAgentServer());
        ImmediateTimeProvider time = new();
        StringWriter console = new();
        SequenceRunner runner = TestAgents.Runner(server, _tools, new AgentLog(time, console), time, rawDisks: _disks);

        RunResult result = await runner.RunAsync(
            s_machineId,
            run,
            null,
            null,
            new DeploymentTokens("session-0", "resume-0"),
            Identity(secureBootEnabled),
            server.Stop.Token);

        return (result, server, console.ToString());
    }

    [Fact]
    public async Task WritesTheImageAndTheSeedThenStartsTheImageFirst()
    {
        (RunResult result, ScriptedAgentServer server, _) = await RunAsync(Run());

        Assert.Equal(new RunResult(RunOutcome.Finished), result);
        Assert.Equal(
            ["list", "clean 0", $"firmware noble-test {FirmwareBootEntry.FallbackLoaderPath} on partition {TestRawImage.EspNumber}", "reboot"],
            _tools.Calls);
        Assert.Equal(RestartInto.Windows, TestAgents.RestartMarker(_tools, new AgentLog(new ImmediateTimeProvider(), TextWriter.Null)).Due);
        Assert.Equal((DeploymentState.Done, RunActivity.Finishing), (server.RunReports[^1].State, server.RunReports[^1].Activity));
        Assert.Equal([StepState.Done, StepState.Done], server.RunReports[^1].Steps.Select(step => step.State));

        // Nothing of the run is kept on the disk it wrote: the image is the disk.
        MemoryRawDisk disk = _disks.Disks[0];
        GptLayout layout = GptLayout.Read(disk.ReadAt(0, RawDiskWriter.HeadBytes));
        Assert.Equal(["EFI", "root", CloudInitSeed.Label], layout.Partitions.Select(partition => partition.Name));
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

    // A run assigned without a name, which a rule's pattern gives it: the values the report that started the run brought
    // name the machine in its seed.
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

    // A seed step with conditions is left to its step, which fails after the image was written.
    [Fact]
    public async Task AFailedSeedLeavesTheBootOrderAndOwesNoRestart()
    {
        WriteCloudInitSeedStep unnamed = s_seed with
        {
            MetaData = "hostname: \"{{ComputerName}}\"",
            Conditions = [new StepCondition(MachineVariableNames.Phase, ConditionOperator.Equals, "WindowsPE")],
        };
        AgentRun run = Run(steps: [s_write, unnamed]) with { ComputerName = null };

        (RunResult result, ScriptedAgentServer server, _) = await RunAsync(run);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal(["list", "clean 0"], _tools.Calls);
        Assert.Null(TestAgents.RestartMarker(_tools, new AgentLog(new ImmediateTimeProvider(), TextWriter.Null)).Due);
        Assert.Equal([StepState.Done, StepState.Failed], server.RunReports[^1].Steps.Select(step => step.State));
    }
}
