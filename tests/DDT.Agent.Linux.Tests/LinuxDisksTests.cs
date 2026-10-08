// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Core.Disks;
using Xunit;

namespace DDT.Agent.Tests;

// The machine's disks from /sys/block, and the clean that erases what a disk held.
public sealed class LinuxDisksTests : IDisposable
{
    private const long Gigabyte = 1000L * 1000 * 1000;

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

    // name leads to path, as the link in /sys/block does on a machine.
    private void AddDisk(string name, long sizeBytes, string path, bool removable = false)
    {
        _tree.AddDisk(name, sizeBytes, removable);
        _leadsTo[name] = path;
    }

    private Task<IReadOnlyList<LocalDisk>> ListAsync() => Disks().ListDisksAsync(TestContext.Current.CancellationToken);

    private static bool IsZero(ReadOnlySpan<byte> bytes) => bytes.IndexOfAnyExcept((byte)0) < 0;

    [Fact]
    public async Task ListsTheDisksWithWhatTheKernelShowsOfThem()
    {
        AddDisk("nvme0n1", 512 * Gigabyte, "/sys/devices/pci0000:00/0000:00:1d.0/0000:3c:00.0/nvme/nvme0/nvme0n1");
        _tree.Write("Samsung SSD 980 PRO 500GB              \n", "sys", "block", "nvme0n1", "device", "model");
        _tree.Write("1\n", "sys", "block", "nvme0n1", "nvme0n1p1", "partition");
        _tree.Write("2\n", "sys", "block", "nvme0n1", "nvme0n1p2", "partition");
        _tree.Write("none\n", "sys", "block", "nvme0n1", "queue", "scheduler");
        AddDisk("sda", 256 * Gigabyte, "/sys/devices/pci0000:00/0000:00:17.0/ata1/host0/target0:0:0/0:0:0:0/block/sda");
        _tree.Write("ATA     \n", "sys", "block", "sda", "device", "vendor");
        _tree.Write("WDC  WDS250G2B0A\n", "sys", "block", "sda", "device", "model");

        IReadOnlyList<LocalDisk> disks = await ListAsync();

        Assert.Equal(
            [
                new LocalDisk(0, "Samsung SSD 980 PRO 500GB", 512 * Gigabyte, StorageBusType.Nvme, 2) { Device = _tree.PathOf("dev", "nvme0n1") },
                new LocalDisk(1, "ATA WDC WDS250G2B0A", 256 * Gigabyte, StorageBusType.Sata, 0) { Device = _tree.PathOf("dev", "sda") },
            ],
            disks);
        Assert.Contains(
            $"Disk 0: Samsung SSD 980 PRO 500GB, {ByteSize.Format(512 * Gigabyte)}, Nvme, 2 partitions, {_tree.PathOf("dev", "nvme0n1")}, removable no: DDT can install on it.",
            _console.ToString(),
            StringComparison.Ordinal);
    }

    // The same rules as in Windows PE: no removable media, nothing on USB, nothing too small for an installation.
    [Fact]
    public async Task LeavesOutWhatWindowsPELeavesOutAndSaysWhy()
    {
        AddDisk("sda", 64 * Gigabyte, "/sys/devices/pci0000:00/0000:00:14.0/usb2/2-1/2-1:1.0/host4/target4:0:0/4:0:0:0/block/sda", removable: true);
        AddDisk("sdb", 500 * Gigabyte, "/sys/devices/pci0000:00/0000:00:14.0/usb2/2-2/2-2:1.0/host5/target5:0:0/5:0:0:0/block/sdb");
        AddDisk("sdc", 8 * Gigabyte, "/sys/devices/pci0000:00/0000:00:17.0/ata2/host1/target1:0:0/1:0:0:0/block/sdc");
        AddDisk("mmcblk0", 64 * Gigabyte, "/sys/devices/platform/80860F14:00/mmc_host/mmc0/mmc0:0001/block/mmcblk0");
        _tree.Write("MMC\n", "sys", "block", "mmcblk0", "device", "type");
        _tree.Write("DF4064\n", "sys", "block", "mmcblk0", "device", "name");

        IReadOnlyList<LocalDisk> disks = await ListAsync();
        string console = _console.ToString();

        LocalDisk card = Assert.Single(disks);
        Assert.Equal((0, "DF4064", StorageBusType.Mmc), (card.Number, card.Model, card.BusType));
        Assert.Contains("removable yes: left out because its media is removable.", console, StringComparison.Ordinal);
        Assert.Contains($"Disk 2: unknown model, {ByteSize.Format(500 * Gigabyte)}, Usb, 0 partitions", console, StringComparison.Ordinal);
        Assert.Contains("left out because it is attached through Usb.", console, StringComparison.Ordinal);
        Assert.Contains("left out because it is smaller than 30 GB.", console, StringComparison.Ordinal);
    }

    // The boot areas of an eMMC, the hidden path of a multipath NVMe, and what is no disk at all.
    [Fact]
    public async Task LooksOnlyAtDisks()
    {
        foreach (string name in new[] { "loop0", "ram0", "sr0", "dm-0", "md0", "zram0", "mmcblk0boot0", "mmcblk0boot1", "nvme0c0n1", "nbd0" })
        {
            AddDisk(name, 100 * Gigabyte, "/sys/devices/virtual/block/" + name);
        }

        AddDisk("vda", 100 * Gigabyte, "/sys/devices/pci0000:00/0000:00:04.0/virtio1/block/vda");

        // What a virtio disk shows as its vendor is the number of its maker, which is no part of a model's name.
        _tree.Write("0x1af4\n", "sys", "block", "vda", "device", "vendor");

        LocalDisk disk = Assert.Single(await ListAsync());

        Assert.Equal((0, (string?)null, StorageBusType.Virtual), (disk.Number, disk.Model, disk.BusType));
    }

    [Fact]
    public async Task ListsNothingAndSaysSoWhereTheKernelShowsNoDisks()
    {
        LinuxDisks none = new(_tree.PathOf("nothing"), new LinuxRawDisks(_tree.Root, _devices), new AgentLog(new ImmediateTimeProvider(), _console));

        Assert.Empty(await none.ListDisksAsync(TestContext.Current.CancellationToken));
        Assert.Contains("The kernel's list of disks cannot be read", _console.ToString(), StringComparison.Ordinal);
    }

    // The old EFI system partition's GUID goes to the run, which reuses the firmware entry that named it.
    [Fact]
    public async Task CleaningZeroesTheTablesAndThePartitionsEndsAndNamesTheSystemPartitionsItErased()
    {
        TestRawImage image = new();
        byte[] before = [.. Enumerable.Repeat((byte)0xAB, 64 * 1024 * 1024)];
        image.Disk.CopyTo(before, 0);
        string device = _tree.Write(before, "dev", "sda");
        AddDisk("sda", before.Length, "/sys/devices/pci0000:00/0000:00:17.0/ata1/host0/target0:0:0/0:0:0:0/block/sda");
        LocalDisk disk = new(0, "Test", before.Length, StorageBusType.Sata, 2) { Device = device };

        IReadOnlyList<Guid> erased = await Disks().CleanAsync(disk, TestContext.Current.CancellationToken);

        byte[] after = File.ReadAllBytes(device);
        const int Mebibyte = 1024 * 1024;
        long rootStart = (TestRawImage.EspFirst + TestRawImage.EspSectors) * GptLayout.SectorSize;
        long rootEnd = (TestRawImage.Sectors - 2048 + 1) * GptLayout.SectorSize;

        Assert.Equal([TestRawImage.EspId], erased);
        Assert.True(IsZero(after.AsSpan(0, Mebibyte)));
        Assert.True(IsZero(after.AsSpan(after.Length - Mebibyte)));
        Assert.True(IsZero(after.AsSpan((int)(TestRawImage.EspFirst * GptLayout.SectorSize), Mebibyte)));
        Assert.True(IsZero(after.AsSpan((int)rootStart, Mebibyte)));
        Assert.True(IsZero(after.AsSpan((int)rootEnd - Mebibyte, Mebibyte)));

        // What lies between stays: only the ends are worth the time.
        Assert.Equal(before.AsSpan((int)rootEnd, 1024).ToArray(), after.AsSpan((int)rootEnd, 1024).ToArray());
        Assert.Equal(1, _devices.Rereads);
        Assert.Contains($"Erasing the partition table of disk 0, {device}: zeroing the first and the last mebibyte of the disk and of 2 partitions.", _console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CleaningADiskWithoutAPartitionTableZeroesItsEndsAndOnlyWarnsWhenTheKernelKeepsTheOldPartitions()
    {
        byte[] before = [.. Enumerable.Repeat((byte)0xCD, 4 * 1024 * 1024)];
        string device = _tree.Write(before, "dev", "vda");
        AddDisk("vda", before.Length, "/sys/devices/pci0000:00/0000:00:04.0/virtio1/block/vda");
        _devices.RereadFailure = new IOException("Device or resource busy", IBlockDevices.Busy);

        IReadOnlyList<Guid> erased = await Disks().CleanAsync(
            new LocalDisk(0, null, before.Length, StorageBusType.Virtual, 0) { Device = device },
            TestContext.Current.CancellationToken);

        byte[] after = File.ReadAllBytes(device);
        Assert.Empty(erased);
        Assert.True(IsZero(after.AsSpan(0, 1024 * 1024)));
        Assert.True(IsZero(after.AsSpan(after.Length - (1024 * 1024))));
        Assert.Equal(0xCD, after[2 * 1024 * 1024]);
        Assert.Contains("did not read the partition table", _console.ToString(), StringComparison.Ordinal);
        Assert.Contains("The disk is erased all the same.", _console.ToString(), StringComparison.Ordinal);
    }
}
