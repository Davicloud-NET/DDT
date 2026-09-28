// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Disks;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class WriteCloudInitSeedStepRunnerTests : IDisposable
{
    private static readonly WriteRawImageStep s_write = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000d2"),
        Name = "Write the disk",
        ImageId = TestRawImage.ImageId,
    };

    private static readonly WriteCloudInitSeedStep s_seed = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000d3"),
        Name = "Seed",
        MetaData = "instance-id: \"{{SmbiosUuid}}\"\nlocal-hostname: \"{{ComputerName}}\"\n",
        UserData = "#cloud-config\nwrite_files:\n  - path: /etc/ddt\n    content: \"{{Manufacturer}} {{Model}} {{SerialNumber}} {{MacAddress}}\"\n",
        NetworkConfig = "version: 2\n",
    };

    private readonly TestRawImage _image = new();
    private readonly StepRunnerFixture _run;

    public WriteCloudInitSeedStepRunnerTests()
    {
        _run = new StepRunnerFixture([s_write, s_seed], [_image.RunImage()]);
        _image.Serve(_run.Server);
    }

    public void Dispose() => _run.Dispose();

    private async Task<IReadOnlyDictionary<string, string>> WrittenAsync() =>
        (await _run.WriteRawImage.RunAsync(s_write, _run.Context(), TestContext.Current.CancellationToken)).Outputs!;

    private static FatVolume Volume(MemoryRawDisk disk, GptPartition partition)
    {
        byte[] bytes = disk.ReadAt(partition.FirstLba * GptLayout.SectorSize, (int)(partition.Sectors * GptLayout.SectorSize));

        return FatVolume.Open(new MemoryStream(bytes), 0, bytes.Length);
    }

    private static string Text(FatVolume volume, string name) => Encoding.UTF8.GetString(volume.ReadFile(volume.Find(name)!, 64 * 1024));

    [Fact]
    public async Task AddsTheSeedAtTheEndOfTheDiskWithTheMachinesValues()
    {
        IReadOnlyDictionary<string, string> written = await WrittenAsync();

        StepResult result = await _run.WriteCloudInitSeed.RunAsync(s_seed, _run.Context(variables: written), TestContext.Current.CancellationToken);

        MemoryRawDisk disk = _run.RawDisks.Disks[0];
        GptLayout layout = GptLayout.Read(disk.ReadAt(0, RawDiskWriter.HeadBytes));
        GptPartition seed = Assert.Single(layout.Partitions, partition => partition.Name == CloudInitSeed.Label);
        Assert.Equal(3, seed.Number);
        Assert.Equal(GptPartitionTypes.BasicData, seed.Type);
        Assert.Equal(CloudInitSeed.SizeBytes / GptLayout.SectorSize, seed.Sectors);
        Assert.True(seed.LastLba > layout.LastUsableLba - GptLayout.AlignmentSectors);
        Assert.Equal(_image.Layout.Partitions, layout.Partitions.Where(partition => partition.Number != 3));
        Assert.Equal("3", result.Outputs![RunVariables.SeedPartition]);

        // The backup table agrees with the primary one.
        Assert.Equal(layout.BackupHeader(), disk.ReadAt(layout.BackupLba * GptLayout.SectorSize, GptLayout.SectorSize));

        FatVolume volume = Volume(disk, seed);
        Assert.Equal("CIDATA", volume.Label);
        Assert.Equal("instance-id: \"4c4c4544-0042-3510-8052-b4c04f4d3232\"\nlocal-hostname: \"PC-042\"\n", Text(volume, "meta-data"));
        Assert.Contains("content: \"Dell Inc. Latitude 5440 SN-1 00:15:5d:01:02:03\"", Text(volume, "user-data"), StringComparison.Ordinal);
        Assert.Equal("version: 2\n", Text(volume, "network-config"));
    }

    [Fact]
    public async Task NeedsARawImageWrittenInThisRun()
    {
        DeploymentStepException refusal = await Assert.ThrowsAsync<DeploymentStepException>(
            () => _run.WriteCloudInitSeed.RunAsync(s_seed, _run.Context(), TestContext.Current.CancellationToken));

        Assert.Equal(WriteCloudInitSeedStepRunner.NoImageMessage, refusal.Message);
    }

    // A seed may use the run's values, such as an input's answer or a value a rule sets, by name. A name the run has no
    // value for stays as it is, as cloud-init's own templates do.
    [Fact]
    public async Task FillsInTheRunsValuesAndLeavesOtherNames()
    {
        IReadOnlyDictionary<string, string> written = await WrittenAsync();
        WriteCloudInitSeedStep seed = s_seed with
        {
            UserData = "## template: jinja\n#cloud-config\nfqdn: \"{{ office | lower }}.example\"\nsite: \"{{Site}}\"\nhost: {{ v1.local_hostname }}\n",
        };

        await _run.WriteCloudInitSeed.RunAsync(
            seed,
            _run.Context(variables: written, values: new Dictionary<string, string> { ["Office"] = "VIE" }),
            TestContext.Current.CancellationToken);

        MemoryRawDisk disk = _run.RawDisks.Disks[0];
        GptPartition partition = GptLayout.Read(disk.ReadAt(0, RawDiskWriter.HeadBytes)).Partitions.Single(candidate => candidate.Name == CloudInitSeed.Label);
        Assert.Equal(
            "## template: jinja\n#cloud-config\nfqdn: \"vie.example\"\nsite: \"{{Site}}\"\nhost: {{ v1.local_hostname }}\n",
            Text(Volume(disk, partition), "user-data"));
    }

    [Fact]
    public async Task NamesAPlaceholderTheRunHasNoValueFor()
    {
        using StepRunnerFixture unnamed = new([s_write, s_seed], [_image.RunImage()]);
        _image.Serve(unnamed.Server);
        StepResult image = await unnamed.WriteRawImage.RunAsync(s_write, unnamed.Context(), TestContext.Current.CancellationToken);
        IReadOnlyDictionary<string, string> written = image.Outputs!;
        RunSession session = new(unnamed.Session.MachineId, unnamed.Session.Run with { ComputerName = null }, unnamed.Session.Tokens)
        {
            Disk = unnamed.Session.Disk,
        };
        WriteCloudInitSeedStepRunner seeds = new(unnamed.RawDisks, session, unnamed.Log, unnamed.Time);

        DeploymentStepException refusal = await Assert.ThrowsAsync<DeploymentStepException>(
            () => seeds.RunAsync(s_seed, unnamed.Context(variables: written), TestContext.Current.CancellationToken));

        Assert.StartsWith("The machine has no value for {{ComputerName}}.", refusal.Message, StringComparison.Ordinal);
    }
}
