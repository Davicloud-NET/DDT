using System.Buffers.Binary;
using System.Text;
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
    public void CountsOnlyTheMbrPartitionsInUse()
    {
        // Four slots, as Windows always lists an MBR table: NTFS, empty, an extended container and a FAT32 drive.
        byte[] layout = Layout(DriveLayoutReader.StyleMbr, (1, 0x07), (0, 0x00), (0, 0x0F), (2, 0x0C));

        Assert.Equal(2, DriveLayoutReader.CountUsedPartitions(layout));
    }

    [Theory]
    [InlineData(0x05)]
    [InlineData(0x0F)]
    [InlineData(0x85)]
    public void AnExtendedContainerIsNoPartition(byte type) =>
        Assert.Equal(0, DriveLayoutReader.CountUsedPartitions(Layout(DriveLayoutReader.StyleMbr, (1, type))));

    [Fact]
    public void CountsTheGptEntriesWithAPartitionNumber()
    {
        byte[] layout = Layout(DriveLayoutReader.StyleGpt, (1, 0), (2, 0), (3, 0), (0, 0));

        Assert.Equal(3, DriveLayoutReader.CountUsedPartitions(layout));
    }

    [Fact]
    public void ARawDiskHasNoPartitions() =>
        Assert.Equal(0, DriveLayoutReader.CountUsedPartitions(Layout(DriveLayoutReader.StyleRaw)));

    [Fact]
    public void RefusesALayoutThatListsMoreThanItHolds()
    {
        byte[] layout = Layout(DriveLayoutReader.StyleGpt, (1, 0));
        BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(4), 2);

        Assert.Throws<ArgumentException>(() => DriveLayoutReader.CountUsedPartitions(layout));
    }

    [Fact]
    public void FindsTheUniqueGuidsOfTheEfiSystemPartitions()
    {
        Guid first = Guid.Parse("7a6b5c4d-3e2f-4a1b-8c9d-0e1f2a3b4c5d");
        Guid second = Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");
        byte[] layout = GptLayout(
            (DriveLayoutReader.EfiSystemPartitionType, first),
            (Guid.Parse("ebd0a0a2-b9e5-4433-87c0-68b6b72699c7"), Guid.Parse("5e2b1a3c-4d6f-4a8b-9c0d-1e2f3a4b5c6d")),
            (DriveLayoutReader.EfiSystemPartitionType, second));

        Assert.Equal([first, second], DriveLayoutReader.EfiSystemPartitionIds(layout));
    }

    [Fact]
    public void AnMbrDiskHasNoEfiSystemPartitionGuids() =>
        Assert.Empty(DriveLayoutReader.EfiSystemPartitionIds(Layout(DriveLayoutReader.StyleMbr, (1, 0xEF))));

    [Fact]
    public void ReadsTheBusTypeRemovableFlagAndModel()
    {
        byte[] descriptor = Descriptor(removable: false, StorageBusType.Scsi, "Msft    ", "Virtual Disk    ");

        StorageDeviceInfo device = StorageDescriptorReader.Read(descriptor);

        Assert.Equal(new StorageDeviceInfo(false, StorageBusType.Scsi, "Msft Virtual Disk"), device);
        Assert.Equal(descriptor.Length, StorageDescriptorReader.ReadSize(descriptor));
    }

    [Fact]
    public void AModelWithoutVendorIsTheProductAlone()
    {
        StorageDeviceInfo device = StorageDescriptorReader.Read(Descriptor(removable: true, StorageBusType.Nvme, null, "Samsung SSD 980 PRO 1TB"));

        Assert.Equal(new StorageDeviceInfo(true, StorageBusType.Nvme, "Samsung SSD 980 PRO 1TB"), device);
    }

    [Fact]
    public void ADiskWithoutNamesHasNoModel() =>
        Assert.Null(StorageDescriptorReader.Read(Descriptor(removable: false, StorageBusType.Sata, null, null)).Model);

    [Fact]
    public void TheDiskSentToTheServerNamesItsBus()
    {
        LocalDisk disk = new(1, "Test disk", Large, StorageBusType.Nvme, 3);

        Assert.Equal(new Contracts.Agents.AgentDisk(1, "Test disk", Large, "Nvme", 3), disk.ToAgentDisk());
        Assert.Equal("Disk 1: Test disk, 256 GB, Nvme, 3 partitions", disk.Describe());
    }

    private static byte[] Layout(int style, params (int Number, byte MbrType)[] entries)
    {
        byte[] layout = new byte[DriveLayoutReader.HeaderLength + (entries.Length * DriveLayoutReader.EntryLength)];
        BinaryPrimitives.WriteInt32LittleEndian(layout, style);
        BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(4), (uint)entries.Length);

        for (int index = 0; index < entries.Length; index++)
        {
            Span<byte> entry = layout.AsSpan(DriveLayoutReader.HeaderLength + (index * DriveLayoutReader.EntryLength));
            BinaryPrimitives.WriteInt32LittleEndian(entry, style);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[24..], (uint)entries[index].Number);
            entry[32] = entries[index].MbrType;
        }

        return layout;
    }

    // Each entry numbered from 1, with its type GUID at 32 and its unique GUID at 48.
    private static byte[] GptLayout(params (Guid Type, Guid Id)[] entries)
    {
        byte[] layout = Layout(DriveLayoutReader.StyleGpt, [.. entries.Select((_, index) => (index + 1, (byte)0))]);

        for (int index = 0; index < entries.Length; index++)
        {
            Span<byte> entry = layout.AsSpan(DriveLayoutReader.HeaderLength + (index * DriveLayoutReader.EntryLength));
            Assert.True(entries[index].Type.TryWriteBytes(entry[32..]));
            Assert.True(entries[index].Id.TryWriteBytes(entry[48..]));
        }

        return layout;
    }

    // STORAGE_DEVICE_DESCRIPTOR with the vendor and product strings after the fixed part, as Windows returns it.
    private static byte[] Descriptor(bool removable, StorageBusType busType, string? vendor, string? product)
    {
        List<byte> strings = [];
        uint vendorOffset = Append(vendor);
        uint productOffset = Append(product);

        byte[] descriptor = [.. new byte[40], .. strings];
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(4), (uint)descriptor.Length);
        descriptor[10] = removable ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(12), vendorOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(16), productOffset);
        BinaryPrimitives.WriteInt32LittleEndian(descriptor.AsSpan(28), (int)busType);

        return descriptor;

        uint Append(string? text)
        {
            if (text is null)
            {
                return 0;
            }

            uint offset = (uint)(40 + strings.Count);
            strings.AddRange(Encoding.ASCII.GetBytes(text));
            strings.Add(0);

            return offset;
        }
    }
}
