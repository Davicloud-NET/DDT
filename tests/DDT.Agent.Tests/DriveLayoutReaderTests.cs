// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class DriveLayoutReaderTests
{
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
    public void FindsThePartitionNumberOfAUniqueGuid()
    {
        Guid system = Guid.Parse("7a6b5c4d-3e2f-4a1b-8c9d-0e1f2a3b4c5d");
        Guid recovery = Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");
        Guid basicData = Guid.Parse("ebd0a0a2-b9e5-4433-87c0-68b6b72699c7");
        byte[] layout = GptLayout(
            (DriveLayoutReader.EfiSystemPartitionType, system),
            (basicData, Guid.Parse("5e2b1a3c-4d6f-4a8b-9c0d-1e2f3a4b5c6d")),
            (basicData, Guid.Parse("6e2b1a3c-4d6f-4a8b-9c0d-1e2f3a4b5c6d")),
            (Guid.Parse("de94bba4-06d1-4d40-a16a-bfd50179d6ac"), recovery));

        Assert.Equal(1u, DriveLayoutReader.PartitionNumberOf(layout, system));
        Assert.Equal(4u, DriveLayoutReader.PartitionNumberOf(layout, recovery));
        Assert.Null(DriveLayoutReader.PartitionNumberOf(layout, Guid.Parse("11111111-2222-4333-8444-555555555555")));
        Assert.Null(DriveLayoutReader.PartitionNumberOf(Layout(DriveLayoutReader.StyleMbr, (1, 0x07)), system));
    }

    [Fact]
    public void AnMbrDiskHasNoEfiSystemPartitionGuids() =>
        Assert.Empty(DriveLayoutReader.EfiSystemPartitionIds(Layout(DriveLayoutReader.StyleMbr, (1, 0xEF))));

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
}
