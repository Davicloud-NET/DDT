// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using DDT.Core.Disks;
using Xunit;

namespace DDT.Core.Tests.Disks;

public sealed class GptLayoutTests
{
    // 8 MiB, like a small cloud image: an EFI system partition and a root file system.
    private const long DiskSectors = 16384;

    private static readonly Guid s_diskId = new("6f1e2a3b-4c5d-4e6f-8a9b-0c1d2e3f4a5b");
    private static readonly Guid s_espId = new("11111111-2222-4333-8444-555555555555");
    private static readonly Guid s_rootId = new("66666666-7777-4888-9999-aaaaaaaaaaaa");

    private static GptLayout CloudImage() =>
        GptLayout.Create(DiskSectors, s_diskId)
            .WithPartition(GptPartitionTypes.EfiSystem, s_espId, "EFI system", 2048, 4095)
            .WithPartition(GptPartitionTypes.LinuxFileSystem, s_rootId, "root", 4096, 14335);

    [Fact]
    public void ReadsWhatItWrote()
    {
        byte[] disk = DiskImages.Write(CloudImage());

        GptLayout read = GptLayout.Read(disk);

        Assert.Equal(s_diskId, read.DiskId);
        Assert.Equal(34, read.FirstUsableLba);
        Assert.Equal(DiskSectors - 34, read.LastUsableLba);
        Assert.Equal(DiskSectors - 1, read.BackupLba);
        Assert.Equal(34 * GptLayout.SectorSize, read.HeadBytes);
        Assert.Equal(
            [
                new GptPartition(1, GptPartitionTypes.EfiSystem, s_espId, 2048, 4095, 0, "EFI system"),
                new GptPartition(2, GptPartitionTypes.LinuxFileSystem, s_rootId, 4096, 14335, 0, "root"),
            ],
            read.Partitions);
        Assert.Equal(14335, read.LastUsedLba);
    }

    [Fact]
    public async Task ReadsFromAStream()
    {
        using MemoryStream disk = new(DiskImages.Write(CloudImage()));

        GptLayout read = await GptLayout.ReadAsync(disk, TestContext.Current.CancellationToken);

        Assert.Equal(2, read.Partitions.Count);
    }

    [Fact]
    public void RefusesADamagedHeader()
    {
        byte[] disk = DiskImages.Write(CloudImage());
        disk[GptLayout.SectorSize + 40] ^= 1;

        Assert.Equal(GptLayout.DamagedMessage, Assert.Throws<InvalidGptException>(() => GptLayout.Read(disk)).Message);
    }

    [Fact]
    public void RefusesDamagedEntries()
    {
        byte[] disk = DiskImages.Write(CloudImage());
        disk[2 * GptLayout.SectorSize + 60] ^= 1;

        Assert.Equal(GptLayout.DamagedMessage, Assert.Throws<InvalidGptException>(() => GptLayout.Read(disk)).Message);
    }

    [Fact]
    public void RefusesOverlappingPartitions()
    {
        GptLayout layout = CloudImage();
        byte[] entries = layout.EntryArray();
        BinaryPrimitives.WriteInt64LittleEndian(entries.AsSpan(128 + 32), 4000);
        byte[] disk = DiskImages.Write(layout);
        DiskImages.Place(disk, 2, entries);
        Span<byte> header = disk.AsSpan(GptLayout.SectorSize, 92);
        BinaryPrimitives.WriteUInt32LittleEndian(header[88..], Crc32.Append(0, entries));
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], Crc32.Append(0, header));

        InvalidGptException refusal = Assert.Throws<InvalidGptException>(() => GptLayout.Read(disk));

        Assert.EndsWith("Partitions 1 and 2 overlap.", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAFileWithoutATable()
    {
        byte[] zeros = new byte[64 * 1024];

        Assert.Equal(0, GptLayout.SectorSizeOf(zeros));
        Assert.Equal(GptLayout.NoTableMessage, Assert.Throws<InvalidGptException>(() => GptLayout.Read(zeros)).Message);
    }

    [Fact]
    public void RecognisesAnImageForFourKilobyteSectors()
    {
        byte[] disk = DiskImages.Write(CloudImage());
        byte[] shifted = new byte[disk.Length];
        disk.AsSpan(GptLayout.SectorSize, GptLayout.SectorSize).CopyTo(shifted.AsSpan(4096));

        Assert.Equal(4096, GptLayout.SectorSizeOf(shifted));
        Assert.Equal(512, GptLayout.SectorSizeOf(disk));
        Assert.Equal(GptLayout.FourKilobyteSectorsMessage, Assert.Throws<InvalidGptException>(() => GptLayout.Read(shifted)).Message);
    }

    [Fact]
    public void MovesTheBackupToTheEndOfALargerDisk()
    {
        const long larger = DiskSectors * 4;

        GptLayout moved = GptLayout.Read(DiskImages.Write(CloudImage())).ForDisk(larger);
        byte[] disk = DiskImages.Write(moved);
        GptLayout read = GptLayout.Read(disk);

        Assert.Equal(larger - 1, read.BackupLba);
        Assert.Equal(larger - 34, read.LastUsableLba);
        Assert.Equal(moved.Partitions, read.Partitions);

        ReadOnlySpan<byte> backup = DiskImages.Sector(disk, larger - 1);
        Assert.Equal(larger - 1, BinaryPrimitives.ReadInt64LittleEndian(backup[24..]));
        Assert.Equal(1, BinaryPrimitives.ReadInt64LittleEndian(backup[32..]));
        Assert.Equal(larger - 33, BinaryPrimitives.ReadInt64LittleEndian(backup[72..]));
        Assert.Equal(DiskImages.Sector(disk, 2).ToArray(), DiskImages.Sector(disk, larger - 33).ToArray());
    }

    [Fact]
    public void RefusesADiskTooSmallForThePartitions()
    {
        GptLayout layout = CloudImage();

        // The last partition ends at 14335, and the backup takes 33 sectors after the usable range.
        Assert.Throws<InvalidOperationException>(() => layout.ForDisk(14335 + 33));
        Assert.Equal(14335, layout.ForDisk(14335 + 34).LastUsableLba);
    }

    [Fact]
    public void AddsAPartitionAtTheEndOnAMebibyteBoundary()
    {
        GptLayout layout = CloudImage().ForDisk(DiskSectors * 2);

        GptLayout seeded = layout.WithPartitionAtEnd(GptPartitionTypes.BasicData, s_diskId, "CIDATA", 4096);
        GptPartition seed = seeded.Partitions[^1];

        Assert.Equal(3, seed.Number);
        Assert.Equal("CIDATA", seed.Name);
        Assert.Equal(0, seed.FirstLba % GptLayout.AlignmentSectors);
        Assert.Equal(4096, seed.Sectors);
        Assert.True(seed.LastLba <= seeded.LastUsableLba);
        Assert.True(seed.LastLba > seeded.LastUsableLba - GptLayout.AlignmentSectors);
        Assert.Equal(seeded.Partitions, GptLayout.Read(DiskImages.Write(seeded)).Partitions);
    }

    [Fact]
    public void RefusesAPartitionThatDoesNotFitAfterTheLast()
    {
        GptLayout layout = CloudImage();

        Assert.Throws<InvalidOperationException>(() => layout.WithPartitionAtEnd(GptPartitionTypes.BasicData, s_diskId, "CIDATA", 4096));
        Assert.Throws<InvalidOperationException>(() => layout.WithPartition(GptPartitionTypes.BasicData, s_diskId, "late", 4095, 4100));
    }

    [Fact]
    public void RefusesAPartitionWhenNoEntryIsFree()
    {
        GptLayout full = GptLayout.Create(DiskSectors, s_diskId, entryCount: 2)
            .WithPartition(GptPartitionTypes.EfiSystem, s_espId, "a", 2048, 4095)
            .WithPartition(GptPartitionTypes.BasicData, s_rootId, "b", 4096, 6143);

        Assert.Equal(1, full.EntrySectors);
        Assert.Throws<InvalidOperationException>(() => full.WithPartition(GptPartitionTypes.BasicData, s_diskId, "c", 8192, 10239));
    }

    [Fact]
    public void KeepsTheBootCodeAndAttributesOfTheImage()
    {
        GptLayout layout = CloudImage();
        byte[] entries = layout.EntryArray();
        BinaryPrimitives.WriteUInt64LittleEndian(entries.AsSpan(48), 0x8000000000000001);
        byte[] image = DiskImages.Write(layout);
        DiskImages.Place(image, 2, entries);
        Span<byte> header = image.AsSpan(GptLayout.SectorSize, 92);
        BinaryPrimitives.WriteUInt32LittleEndian(header[88..], Crc32.Append(0, entries));
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], Crc32.Append(0, header));
        image.AsSpan(0, 440).Fill(0x90);

        GptLayout read = GptLayout.Read(image).ForDisk(DiskSectors * 2);
        byte[] mbr = read.ProtectiveMbr(image);

        Assert.Equal(0x8000000000000001, read.Partitions[0].Attributes);
        Assert.Equal(entries, read.EntryArray());
        Assert.Equal(image.AsSpan(0, 446).ToArray(), mbr.AsSpan(0, 446).ToArray());
        Assert.Equal(0xEE, mbr[446 + 4]);
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(mbr.AsSpan(446 + 8)));
        Assert.Equal((uint)(DiskSectors * 2 - 1), BinaryPrimitives.ReadUInt32LittleEndian(mbr.AsSpan(446 + 12)));
        Assert.Equal(new byte[48], mbr.AsSpan(462, 48).ToArray());
        Assert.Equal([0x55, 0xAA], mbr.AsSpan(510).ToArray());
    }

    [Fact]
    public void CapsTheProtectiveMbrForDisksOverTwoTebibytes()
    {
        GptLayout layout = CloudImage().ForDisk(8L * 1024 * 1024 * 1024);

        byte[] mbr = layout.ProtectiveMbr(default);

        Assert.Equal(uint.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(mbr.AsSpan(446 + 12)));
        Assert.Equal(new byte[446], mbr.AsSpan(0, 446).ToArray());
    }
}
