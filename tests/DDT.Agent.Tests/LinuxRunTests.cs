// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

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

    private async Task<(RunResult Result, ScriptedAgentServer Server, string Console)> RunAsync(AgentRun run, bool? secureBootEnabled = true)
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());
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

    [Fact]
    public async Task AFailedSeedLeavesTheBootOrderAndOwesNoRestart()
    {
        WriteCloudInitSeedStep unnamed = s_seed with { MetaData = "hostname: \"{{ComputerName}}\"" };
        AgentRun run = Run(steps: [s_write, unnamed]) with { ComputerName = null };

        (RunResult result, ScriptedAgentServer server, _) = await RunAsync(run);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal(["list", "clean 0"], _tools.Calls);
        Assert.Null(TestAgents.RestartMarker(_tools, new AgentLog(new ImmediateTimeProvider(), TextWriter.Null)).Due);
        Assert.Equal([StepState.Done, StepState.Failed], server.RunReports[^1].Steps.Select(step => step.State));
    }
}
