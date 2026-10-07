// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class DiskEligibilityTests
{
    private const long Large = 256L * 1024 * 1024 * 1024;

    [Theory]
    [InlineData(StorageBusType.Nvme)]
    [InlineData(StorageBusType.Sata)]
    [InlineData(StorageBusType.Scsi)]
    [InlineData(StorageBusType.Sas)]
    [InlineData(StorageBusType.Raid)]
    [InlineData(StorageBusType.Virtual)]
    [InlineData(StorageBusType.Mmc)]
    [InlineData(StorageBusType.Sd)]
    public void AnInternalDiskIsEligible(StorageBusType busType) =>
        Assert.Null(DiskEligibility.ExclusionReason(removableMedia: false, busType, Large));

    [Theory]
    [InlineData(StorageBusType.Usb)]
    [InlineData(StorageBusType.Ieee1394)]
    [InlineData(StorageBusType.IScsi)]
    [InlineData(StorageBusType.FileBackedVirtual)]
    [InlineData(StorageBusType.Spaces)]
    public void ExternalAndVirtualBusesAreLeftOut(StorageBusType busType) =>
        Assert.Equal($"it is attached through {busType}", DiskEligibility.ExclusionReason(removableMedia: false, busType, Large));

    [Fact]
    public void RemovableMediaIsLeftOut() =>
        Assert.Equal("its media is removable", DiskEligibility.ExclusionReason(removableMedia: true, StorageBusType.Sd, Large));

    [Fact]
    public void ADiskBelow30GbIsLeftOutAndOneOfExactly30GbIsNot()
    {
        Assert.Equal("it is smaller than 30 GB", DiskEligibility.ExclusionReason(false, StorageBusType.Mmc, DiskEligibility.MinimumSizeBytes - 1));
        Assert.Null(DiskEligibility.ExclusionReason(false, StorageBusType.Mmc, DiskEligibility.MinimumSizeBytes));
    }

    [Fact]
    public void AThirtyTwoGigabyteEmmcIsEligible() =>
        Assert.Null(DiskEligibility.ExclusionReason(removableMedia: false, StorageBusType.Mmc, 61_071_360L * 512));

    [Fact]
    public void TheDiskSentToTheServerNamesItsBus()
    {
        LocalDisk disk = new(1, "Test disk", Large, StorageBusType.Nvme, 3);

        Assert.Equal(new Contracts.Agents.AgentDisk(1, "Test disk", Large, "Nvme", 3), disk.ToAgentDisk());
        Assert.Equal("Disk 1: Test disk, 256 GB, Nvme, 3 partitions", disk.Describe());
    }

    [Fact]
    public void ADiskWithADeviceShowsItAndIsToldApartByIt()
    {
        LocalDisk disk = new(1, "Test disk", Large, StorageBusType.Nvme, 3) { Device = "/dev/nvme0n1" };

        Assert.Equal("Disk 1: Test disk, 256 GB, Nvme, 3 partitions, /dev/nvme0n1", disk.Describe());
        Assert.True(disk.IsSameDiskAs(disk with { PartitionCount = 0 }));
        Assert.False(disk.IsSameDiskAs(disk with { Device = "/dev/nvme1n1" }));
    }
}
