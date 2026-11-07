// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// Which disks count as internal where Linux reports a disk otherwise than Windows does. The paths are those of real
// machines, shortened.
public sealed class LinuxDiskEligibilityTests : IDisposable
{
    private const long Gigabyte = 1000L * 1000 * 1000;
    private const string Nvme = "/sys/devices/pci0000:00/0000:00:1d.0/0000:3c:00.0/nvme/nvme0/nvme0n1";
    private const string BuiltInReader = "/sys/devices/pci0000:00/0000:00:1c.0/0000:02:00.0/mmc_host/mmc0/mmc0:aaaa/block/mmcblk0";

    private readonly LinuxTree _tree = new();
    private readonly FileBlockDevices _devices = new();
    private readonly StringWriter _console = new();
    private readonly Dictionary<string, string> _leadsTo = new(StringComparer.Ordinal);

    public void Dispose() => _tree.Dispose();

    private LinuxDisks Disks() => new(
        _tree.Root,
        new LinuxRawDisks(_tree.Root, _devices, _ => { }),
        new AgentLog(new ImmediateTimeProvider(), _console),
        block => _leadsTo.GetValueOrDefault(Path.GetFileName(block), block));

    private void AddDisk(string name, long sizeBytes, string path, bool removable = false, string? cardType = null)
    {
        _tree.AddDisk(name, sizeBytes, removable);
        _leadsTo[name] = path;

        if (cardType is not null)
        {
            _tree.Write(cardType + "\n", "sys", "block", name, "device", "type");
        }
    }

    private Task<IReadOnlyList<LocalDisk>> ListAsync() => Disks().ListDisksAsync(TestContext.Current.CancellationToken);

    // The kernel shows removable 0 for a card in a reader on the PC's own SD host controller.
    [Fact]
    public async Task ACardInABuiltInReaderIsLeftOutNextToTheInternalDisk()
    {
        AddDisk("mmcblk0", 64 * Gigabyte, BuiltInReader, cardType: "SD");
        _tree.Write("SD64G\n", "sys", "block", "mmcblk0", "device", "name");
        AddDisk("nvme0n1", 512 * Gigabyte, Nvme);

        LocalDisk disk = Assert.Single(await ListAsync());

        Assert.Equal((1, StorageBusType.Nvme), (disk.Number, disk.BusType));
        Assert.Contains(
            $"Disk 0: SD64G, {ByteSize.Format(64 * Gigabyte)}, Sd, 0 partitions, {_tree.PathOf("dev", "mmcblk0")}, removable no: " +
            "left out because it is an SD card, which Linux does not mark as removable.",
            _console.ToString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SD")]
    [InlineData("SDcombo")]
    [InlineData("SDIO")]
    public async Task ACardThatIsTheOnlyDiskLeavesNoDiskToInstallOn(string cardType)
    {
        AddDisk("mmcblk0", 128 * Gigabyte, BuiltInReader, cardType: cardType);

        Assert.Empty(await ListAsync());
    }

    // What says nothing of its kind is not taken for built in.
    [Fact]
    public async Task ACardWhoseKindTheKernelDoesNotShowIsLeftOut()
    {
        AddDisk("mmcblk0", 128 * Gigabyte, BuiltInReader);

        Assert.Empty(await ListAsync());
        Assert.Contains(LinuxDiskEligibility.SdCardReason, _console.ToString(), StringComparison.Ordinal);
    }

    // Cheap laptops have nothing else. Its boot areas and its protected area are no disks.
    [Fact]
    public async Task ABuiltInEmmcStaysADiskToInstallOn()
    {
        AddDisk("mmcblk0", 61_071_360L * 512, "/sys/devices/platform/80860F14:00/mmc_host/mmc0/mmc0:0001/block/mmcblk0", cardType: "MMC");
        AddDisk("mmcblk0boot0", 4 * 1024 * 1024, "/sys/devices/platform/80860F14:00/mmc_host/mmc0/mmc0:0001/block/mmcblk0/mmcblk0boot0");
        AddDisk("mmcblk0rpmb", 4 * 1024 * 1024, "/sys/devices/platform/80860F14:00/mmc_host/mmc0/mmc0:0001/block/mmcblk0/mmcblk0rpmb");
        AddDisk("mmcblk1", 64 * Gigabyte, BuiltInReader.Replace("mmcblk0", "mmcblk1", StringComparison.Ordinal), cardType: "SD");

        LocalDisk disk = Assert.Single(await ListAsync());

        Assert.Equal((0, StorageBusType.Mmc), (disk.Number, disk.BusType));
    }

    // An SSD in a USB case answers as a SCSI disk whose media is fixed, through usb-storage or through uas, and so
    // does an NVMe disk in such a case. A reader on USB shows a disk per slot, of no size while the slot is empty.
    [Theory]
    [InlineData("/sys/devices/pci0000:00/0000:00:14.0/usb2/2-3/2-3:1.0/host6/target6:0:0/6:0:0:0/block/sda", false, 500, "it is attached through Usb")]
    [InlineData("/sys/devices/pci0000:00/0000:00:0d.0/usb4/4-1/4-1.2/4-1.2:1.0/host7/target7:0:0/7:0:0:0/block/sda", false, 1000, "it is attached through Usb")]
    [InlineData("/sys/devices/platform/xhci-hcd.1.auto/usb1/1-1/1-1:1.0/host0/target0:0:0/0:0:0:0/block/sda", false, 256, "it is attached through Usb")]
    [InlineData("/sys/devices/pci0000:00/0000:00:14.0/usb1/1-8/1-8:1.0/host5/target5:0:0/5:0:0:0/block/sda", true, 64, "its media is removable")]
    [InlineData("/sys/devices/pci0000:00/0000:00:14.0/usb1/1-8/1-8:1.0/host5/target5:0:0/5:0:0:1/block/sda", true, 0, "its media is removable")]
    public async Task WhatIsAttachedThroughUsbIsLeftOutWhateverItsMediaSays(string path, bool removable, int gigabytes, string reason)
    {
        AddDisk("sda", gigabytes * Gigabyte, path, removable);

        Assert.Empty(await ListAsync());
        Assert.Contains($"left out because {reason}.", _console.ToString(), StringComparison.Ordinal);
    }

    // The kernel cannot write to it, so a run that chose it would fail after the server was told it starts.
    [Fact]
    public async Task ADiskTheKernelHasReadOnlyIsLeftOut()
    {
        AddDisk("nvme0n1", 512 * Gigabyte, Nvme);
        _tree.Write("1\n", "sys", "block", "nvme0n1", "ro");
        AddDisk("sda", 256 * Gigabyte, "/sys/devices/pci0000:00/0000:00:17.0/ata1/host0/target0:0:0/0:0:0:0/block/sda");
        _tree.Write("0\n", "sys", "block", "sda", "ro");

        LocalDisk disk = Assert.Single(await ListAsync());

        Assert.Equal(StorageBusType.Sata, disk.BusType);
        Assert.Contains("Nvme, 0 partitions, " + _tree.PathOf("dev", "nvme0n1") + ", removable no: left out because the kernel has it read-only.", _console.ToString(), StringComparison.Ordinal);
    }

    // As in Windows PE: a disk on PCI Express is an internal one, also where the slot is in a case on Thunderbolt.
    [Fact]
    public async Task AnNvmeDiskInAThunderboltCaseCountsAsInternalAsItDoesInWindowsPE()
    {
        AddDisk("nvme1n1", 1000 * Gigabyte, "/sys/devices/pci0000:00/0000:00:07.0/0000:01:00.0/0000:02:01.0/0000:03:00.0/nvme/nvme1/nvme1n1");

        Assert.Equal(StorageBusType.Nvme, Assert.Single(await ListAsync()).BusType);
    }

    [Theory]
    [InlineData(false, false, StorageBusType.Sd, LinuxDiskEligibility.SdCardReason)]
    [InlineData(true, false, StorageBusType.Sd, "its media is removable")]
    [InlineData(false, true, StorageBusType.Usb, "it is attached through Usb")]
    [InlineData(false, true, StorageBusType.Nvme, LinuxDiskEligibility.ReadOnlyReason)]
    [InlineData(false, false, StorageBusType.Mmc, null)]
    [InlineData(false, false, StorageBusType.Nvme, null)]
    public void SaysTheFirstReasonThatLeavesADiskOut(bool removable, bool readOnly, StorageBusType bus, string? reason) =>
        Assert.Equal(reason, LinuxDiskEligibility.ExclusionReason(removable, readOnly, bus, 64 * Gigabyte));

    // The rule both systems share stays as Windows PE has it: there a card in a built-in reader says it is removable.
    [Fact]
    public void WindowsPEKeepsItsRule() =>
        Assert.Null(DiskEligibility.ExclusionReason(removableMedia: false, StorageBusType.Sd, 64 * Gigabyte));

    // A run nobody chose a disk for takes the only disk there is. A card must never be that disk.
    [Fact]
    public async Task ARunAssignedOnTheWebIsRefusedWhereACardIsTheOnlyDiskAndNothingIsErased()
    {
        byte[] content = [.. Enumerable.Repeat((byte)0xC4, 1024 * 1024)];
        string device = _tree.Write(content, "dev", "mmcblk0");
        AddDisk("mmcblk0", 64 * Gigabyte, BuiltInReader, cardType: "SD");
        using FakeDeploymentTools tools = new();
        TestRawImage image = new();
        ScriptedAgentServer server = image.Serve(new ScriptedAgentServer());
        ImmediateTimeProvider time = new();
        TestRunnerOptions options = new() { Disks = Disks(), RawDisks = new LinuxRawDisks(_tree.Root, _devices, _ => { }), Environment = AgentEnvironment.LinuxBoot };
        SequenceRunner runner = TestAgents.Runner(server, tools, new AgentLog(time, _console), time, options);
        WriteRawImageStep write = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000e001"), Name = "Write the disk", ImageId = TestRawImage.ImageId };
        AgentRun run = new(TestRuns.RunId, DeploymentState.Assigned, "Install Linux", new SequenceDefinition(SequenceDefinition.CurrentVersion, [write]), [image.RunImage()], [], null, "LINUX-01", false);

        RunResult result = await runner.RunAsync(
            new RunRequest(Guid.NewGuid(), run, null, null, new DeploymentTokens("session-0", "resume-0"), new DryRunMachineIdentityReader(1).Read()),
            server.Stop.Token);

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal(EnvironmentTexts.LinuxNoDisk, server.RunReports[^1].Error);
        Assert.Empty(_devices.Opened);
        Assert.Equal(content, File.ReadAllBytes(device));
    }
}
