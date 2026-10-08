// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

// How a disk is attached, from where the kernel put its device, and which entries of /sys/block are disks.
public sealed class LinuxBusTypeTests
{
    // The paths are those of real machines and virtual machines, shortened.
    [Theory]
    [InlineData("nvme0n1", "/sys/devices/pci0000:00/0000:00:1d.0/0000:3c:00.0/nvme/nvme0/nvme0n1", null, StorageBusType.Nvme)]
    [InlineData("nvme0n1", "/sys/devices/pci0000:00/0000:00:0e.0/pci10000:e0/10000:e0:06.0/10000:e1:00.0/nvme/nvme0/nvme0n1", null, StorageBusType.Nvme)]
    [InlineData("sda", "/sys/devices/pci0000:00/0000:00:17.0/ata1/host0/target0:0:0/0:0:0:0/block/sda", null, StorageBusType.Sata)]
    [InlineData("sdb", "/sys/devices/pci0000:00/0000:00:14.0/usb2/2-1/2-1:1.0/host4/target4:0:0/4:0:0:0/block/sdb", null, StorageBusType.Usb)]
    [InlineData("sda", "/sys/devices/pci0000:00/0000:00:01.0/0000:01:00.0/host0/port-0:0/end_device-0:0/target0:0:0/0:0:0:0/block/sda", null, StorageBusType.Sas)]
    [InlineData("sdc", "/sys/devices/platform/host3/session1/target3:0:0/3:0:0:0/block/sdc", null, StorageBusType.IScsi)]
    [InlineData("sdd", "/sys/devices/pci0000:00/0000:00:03.0/host5/rport-5:0-0/target5:0:0/5:0:0:0/block/sdd", null, StorageBusType.Fibre)]
    [InlineData("sda", "/sys/devices/pci0000:00/0000:00:10.0/host2/target2:0:0/2:0:0:0/block/sda", null, StorageBusType.Scsi)]
    [InlineData("vda", "/sys/devices/pci0000:00/0000:00:04.0/virtio1/block/vda", null, StorageBusType.Virtual)]
    [InlineData("sda", "/sys/devices/pci0000:00/0000:00:05.0/virtio2/host0/target0:0:0/0:0:0:0/block/sda", null, StorageBusType.Virtual)]
    [InlineData("sda", "/sys/devices/LNXSYSTM:00/LNXSYBUS:00/ACPI0004:00/VMBUS:00/f8b3781a-1e82-4818-a1c3-63d806ec15bb/host0/target0:0:0/0:0:0:0/block/sda", null, StorageBusType.Virtual)]
    [InlineData("xvda", "/sys/devices/vbd-51712/block/xvda", null, StorageBusType.Virtual)]
    [InlineData("mmcblk0", "/sys/devices/platform/80860F14:00/mmc_host/mmc0/mmc0:0001/block/mmcblk0", "MMC", StorageBusType.Mmc)]
    [InlineData("mmcblk1", "/sys/devices/pci0000:00/0000:00:1c.0/0000:02:00.0/mmc_host/mmc1/mmc1:aaaa/block/mmcblk1", "SD", StorageBusType.Sd)]
    [InlineData("sda", @"C:\tree\sys\devices\pci0000:00\ata3\host2\block\sda", null, StorageBusType.Sata)]
    public void NamesTheBusAsWindowsDoes(string name, string path, string? cardType, StorageBusType expected) =>
        Assert.Equal(expected, LinuxBusType.Of(name, path, cardType));

    [Theory]
    [InlineData("sda", true)]
    [InlineData("sdaa", true)]
    [InlineData("nvme0n1", true)]
    [InlineData("nvme12n3", true)]
    [InlineData("mmcblk0", true)]
    [InlineData("vdb", true)]
    [InlineData("xvda", true)]
    [InlineData("sda1", false)]
    [InlineData("nvme0n1p1", false)]
    [InlineData("nvme0c0n1", false)]
    [InlineData("mmcblk0boot0", false)]
    [InlineData("mmcblk0rpmb", false)]
    [InlineData("loop0", false)]
    [InlineData("sr0", false)]
    [InlineData("dm-0", false)]
    [InlineData("md127", false)]
    [InlineData("zram0", false)]
    public void TakesOnlyWholeDisksForDisks(string name, bool disk) => Assert.Equal(disk, DiskNames.IsDisk(name));

    // The numbers a technician sees follow the names as a person would order them, one kind of disk after the other.
    [Fact]
    public void OrdersTheDisksByKindAndThenAsAPersonCounts()
    {
        string[] names = ["sdaa", "nvme10n1", "sdb", "mmcblk1", "nvme2n1", "sda", "vda", "sdz", "nvme0n2", "nvme0n1", "mmcblk0", "xvda"];

        Assert.Equal(
            ["mmcblk0", "mmcblk1", "nvme0n1", "nvme0n2", "nvme2n1", "nvme10n1", "sda", "sdb", "sdz", "sdaa", "vda", "xvda"],
            names.Order(DiskNames.Order));
    }
}
