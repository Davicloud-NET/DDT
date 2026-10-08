// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

// A disk opened by its device, with a file in the device's place.
public sealed class LinuxRawDisksTests : IDisposable
{
    private const int Megabyte = 1024 * 1024;

    private readonly LinuxTree _tree = new();
    private readonly FileBlockDevices _devices = new();
    private readonly List<TimeSpan> _waits = [];

    public void Dispose() => _tree.Dispose();

    private LocalDisk Disk(string name = "nvme0n1", int length = 4 * Megabyte, int sectorSize = 512)
    {
        string device = _tree.Write(new byte[length], "dev", name);
        _tree.AddDisk(name, length, sectorSize: sectorSize, model: "Test");

        return new LocalDisk(0, "Test", length, StorageBusType.Nvme, 0) { Device = device };
    }

    private LinuxRawDisks Disks() => new(_tree.Root, _devices, _waits.Add);

    [Fact]
    public void TakesTheDisksLengthAndSectorSizeFromTheKernel()
    {
        using IRawDisk small = Disks().Open(Disk());
        using IRawDisk large = Disks().Open(Disk("sda", 8 * Megabyte, sectorSize: 4096));

        Assert.Equal((4L * Megabyte, 512), (small.Length, small.SectorSize));
        Assert.Equal((8L * Megabyte, 4096), (large.Length, large.SectorSize));
    }

    [Fact]
    public void WritesAndReadsWholeSectorsAndFlushes()
    {
        LocalDisk disk = Disk();
        byte[] written = [.. Enumerable.Range(0, 1024).Select(index => (byte)index)];
        byte[] read = new byte[1024];

        using (IRawDisk raw = Disks().Open(disk))
        {
            raw.Write(2 * Megabyte, written);
            raw.Flush();
            raw.Read(2 * Megabyte, read);
            raw.UpdateProperties();

            Assert.Throws<ArgumentException>(() => raw.Write(100, written));
            Assert.Throws<ArgumentException>(() => raw.Write(0, written.AsSpan(0, 100)));
            Assert.Throws<ArgumentException>(() => raw.Write((4 * Megabyte) - 512, written));
        }

        Assert.Equal(written, read);
        Assert.Equal(written, File.ReadAllBytes(disk.Device ?? "").AsSpan(2 * Megabyte, 1024).ToArray());
        Assert.Equal(1, _devices.Rereads);
    }

    // The kernel's device manager looks at a disk whose partitions just changed, and lets go within moments.
    [Fact]
    public void WaitsBrieflyForADiskSomethingElseHolds()
    {
        LocalDisk disk = Disk();
        _devices.BusyTimes = 3;

        using IRawDisk raw = Disks().Open(disk);

        Assert.Equal(4, _devices.Opened.Count);
        Assert.Equal([.. Enumerable.Repeat(TimeSpan.FromMilliseconds(250), 3)], _waits);
    }

    [Fact]
    public void SaysWhyADiskThatStaysInUseCannotBeWritten()
    {
        LocalDisk disk = Disk();
        _devices.BusyTimes = int.MaxValue;

        DeploymentStepException refusal = Assert.Throws<DeploymentStepException>(() => Disks().Open(disk));

        Assert.Equal(
            $"{disk.Device} is in use: a file system on it is mounted, or a RAID or volume group holds it. Nothing was written to it.",
            refusal.Message);
        Assert.Equal(20, _devices.Opened.Count);
        Assert.Equal(TimeSpan.FromSeconds(4.75), _waits.Aggregate(TimeSpan.Zero, (sum, wait) => sum + wait));
    }

    [Fact]
    public void SaysThatADiskTheKernelNoLongerListsCannotBeOpened()
    {
        LocalDisk gone = new(0, "Test", 4 * Megabyte, StorageBusType.Usb, 0) { Device = _tree.PathOf("dev", "sdz") };
        LocalDisk unnamed = new(1, "Test", 4 * Megabyte, StorageBusType.Nvme, 0);

        Assert.Contains("The kernel no longer lists", Assert.Throws<DeploymentStepException>(() => Disks().Open(gone)).Message, StringComparison.Ordinal);
        Assert.Equal("Disk 1 has no device to open it by.", Assert.Throws<DeploymentStepException>(() => Disks().Open(unnamed)).Message);
        Assert.Empty(_devices.Opened);
    }

    // Between the choice and the write another disk can have got the name: one unplugged, another plugged in.
    [Fact]
    public void RefusesADiskThatIsNoLongerTheOneThatWasChosen()
    {
        LocalDisk chosen = Disk();
        LocalDisk larger = chosen with { SizeBytes = 8 * Megabyte };
        LocalDisk other = chosen with { Model = "Other" };
        LocalDisk unnamed = chosen with { Model = null };

        Assert.Equal(
            $"{chosen.Device} is not the disk that was chosen any more: that was Test with 8388608 bytes, and this is Test with 4194304 bytes. " +
            "Nothing was written to it. Start the run again.",
            Assert.Throws<DeploymentStepException>(() => Disks().Open(larger)).Message);
        Assert.Contains("that was Other with 4194304 bytes, and this is Test with", Assert.Throws<DeploymentStepException>(() => Disks().Open(other)).Message, StringComparison.Ordinal);
        Assert.Contains("that was unknown model with", Assert.Throws<DeploymentStepException>(() => Disks().Open(unnamed)).Message, StringComparison.Ordinal);
        Assert.Empty(_devices.Opened);
    }

    // The kernel keeps the partitions it knows while one of them is in use. The caller only warns of that.
    [Fact]
    public void TellsTheCallerWhenTheKernelKeepsTheOldPartitionTable()
    {
        _devices.RereadFailure = new IOException("Device or resource busy", IBlockDevices.Busy);
        LocalDisk disk = Disk();

        using IRawDisk raw = Disks().Open(disk);
        DeploymentStepException refusal = Assert.Throws<DeploymentStepException>(raw.UpdateProperties);

        Assert.Equal($"The kernel did not read the partition table of {disk.Device} again (Device or resource busy).", refusal.Message);
    }
}
