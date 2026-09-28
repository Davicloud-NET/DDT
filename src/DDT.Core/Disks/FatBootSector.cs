// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;

namespace DDT.Core.Disks;

// The BIOS parameter block of a FAT boot sector, as FatVolume reads it and FatVolumeBuilder writes it.
internal sealed record FatBootSector(
    FatType Type,
    int BytesPerSector,
    int SectorsPerCluster,
    int ReservedSectors,
    int FatCount,
    int RootEntries,
    long TotalSectors,
    long FatSectors)
{
    public const int Size = 512;
    public const byte FixedDiskMedia = 0xF8;

    private const int DirectoryEntryBytes = 32;
    private const int MaxFatBytes = 16 * 1024 * 1024;
    private const byte ExtendedSignature = 0x29;

    // FAT32 only: FAT12 and FAT16 keep the root directory in its own sectors.
    public uint RootCluster { get; init; }

    public long HiddenSectors { get; init; }

    public uint SerialNumber { get; init; }

    // Without padding; "" when the sector has no extended signature.
    public string Label { get; init; } = "";

    public int RootSectors => ((RootEntries * DirectoryEntryBytes) + BytesPerSector - 1) / BytesPerSector;

    public long RootDirectorySector => ReservedSectors + (FatCount * FatSectors);

    public long FirstDataSector => RootDirectorySector + RootSectors;

    public long ClusterCount => (TotalSectors - FirstDataSector) / SectorsPerCluster;

    public int ClusterBytes => SectorsPerCluster * BytesPerSector;

    // The volume is length bytes long and comes from an uploaded file, so every size is checked and capped.
    public static FatBootSector Parse(ReadOnlySpan<byte> boot, long length)
    {
        if (boot[510] != 0x55 || boot[511] != 0xAA)
        {
            throw new InvalidDataException("The partition holds no FAT file system.");
        }

        // The type follows from the other fields, so it is settled once they are checked.
        ushort fatSectors16 = BinaryPrimitives.ReadUInt16LittleEndian(boot[22..]);
        FatBootSector fields = new(
            FatType.Fat12,
            BinaryPrimitives.ReadUInt16LittleEndian(boot[11..]),
            boot[13],
            BinaryPrimitives.ReadUInt16LittleEndian(boot[14..]),
            boot[16],
            BinaryPrimitives.ReadUInt16LittleEndian(boot[17..]),
            BinaryPrimitives.ReadUInt16LittleEndian(boot[19..]) is ushort small and not 0 ? small : BinaryPrimitives.ReadUInt32LittleEndian(boot[32..]),
            fatSectors16 != 0 ? fatSectors16 : BinaryPrimitives.ReadUInt32LittleEndian(boot[36..]));

        long clusters = fields.CheckGeometry(length);

        // As Linux and EDK2 do: a volume without a 16-bit FAT size is FAT32 whatever its cluster count, as mkfs.fat -F
        // 32 makes on a small partition. Otherwise the cluster count decides between FAT12 and FAT16.
        FatBootSector sector = fields with
        {
            Type = fatSectors16 == 0 ? FatType.Fat32 : clusters <= FatVolume.MaxFatClusters12 ? FatType.Fat12 : FatType.Fat16,
        };
        sector.CheckTypeLimits();

        bool fat32 = sector.Type == FatType.Fat32;
        int extended = fat32 ? 64 : 36;

        return sector with
        {
            RootCluster = fat32 ? BinaryPrimitives.ReadUInt32LittleEndian(boot[44..]) : 0,
            HiddenSectors = BinaryPrimitives.ReadUInt32LittleEndian(boot[28..]),
            SerialNumber = boot[extended + 2] == ExtendedSignature ? BinaryPrimitives.ReadUInt32LittleEndian(boot[(extended + 3)..]) : 0,
            Label = boot[extended + 2] == ExtendedSignature ? Encoding.ASCII.GetString(boot.Slice(extended + 7, 11)).TrimEnd(' ') : "",
        };
    }

    // Writes the first sector; the FAT32 FSInfo sector and backup boot sector are FatVolumeBuilder's.
    public void Write(Span<byte> boot)
    {
        bool fat32 = Type == FatType.Fat32;
        boot[0] = 0xEB;
        boot[1] = fat32 ? (byte)0x58 : (byte)0x3C;
        boot[2] = 0x90;
        "MSWIN4.1"u8.CopyTo(boot[3..]);
        BinaryPrimitives.WriteUInt16LittleEndian(boot[11..], (ushort)BytesPerSector);
        boot[13] = (byte)SectorsPerCluster;
        BinaryPrimitives.WriteUInt16LittleEndian(boot[14..], (ushort)ReservedSectors);
        boot[16] = (byte)FatCount;
        BinaryPrimitives.WriteUInt16LittleEndian(boot[17..], (ushort)RootEntries);

        if (TotalSectors < 0x10000 && !fat32)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(boot[19..], (ushort)TotalSectors);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(boot[32..], (uint)TotalSectors);
        }

        boot[21] = FixedDiskMedia;
        BinaryPrimitives.WriteUInt16LittleEndian(boot[24..], 63);
        BinaryPrimitives.WriteUInt16LittleEndian(boot[26..], 255);
        BinaryPrimitives.WriteUInt32LittleEndian(boot[28..], (uint)HiddenSectors);

        int extended = fat32 ? 64 : 36;

        if (fat32)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(boot[36..], (uint)FatSectors);
            BinaryPrimitives.WriteUInt32LittleEndian(boot[44..], RootCluster);
            BinaryPrimitives.WriteUInt16LittleEndian(boot[48..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(boot[50..], 6);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(boot[22..], (ushort)FatSectors);
        }

        boot[extended] = 0x80;
        boot[extended + 2] = ExtendedSignature;
        BinaryPrimitives.WriteUInt32LittleEndian(boot[(extended + 3)..], SerialNumber);
        Encoding.ASCII.GetBytes(Label.PadRight(FatNames.ShortNameLength), boot[(extended + 7)..]);
        Encoding.ASCII.GetBytes(Type switch { FatType.Fat12 => "FAT12   ", FatType.Fat16 => "FAT16   ", _ => "FAT32   " }, boot[(extended + 18)..]);
        boot[510] = 0x55;
        boot[511] = 0xAA;
    }

    // Returns the cluster count once it is known to fit a uint with room for the two reserved entries.
    private long CheckGeometry(long length)
    {
        if (BytesPerSector is not (512 or 1024 or 2048 or 4096)
            || SectorsPerCluster is 0 or > 128
            || !int.IsPow2(SectorsPerCluster)
            || ReservedSectors == 0
            || FatCount == 0
            || FatSectors == 0
            || TotalSectors * BytesPerSector > length)
        {
            throw new InvalidDataException("The partition holds no FAT file system DDT can read.");
        }

        if (FirstDataSector >= TotalSectors)
        {
            throw new InvalidDataException("The partition's FAT file system is damaged.");
        }

        return ClusterCount > uint.MaxValue - 2
            ? throw new InvalidDataException("The partition's FAT file system is damaged or larger than DDT reads.")
            : ClusterCount;
    }

    private void CheckTypeLimits()
    {
        if (Type == FatType.Fat16 && ClusterCount > FatVolume.MaxFatClusters16)
        {
            throw new InvalidDataException("The partition's FAT file system is damaged.");
        }

        long fatBytes = FatTable.BytesFor(Type, ClusterCount);

        if (fatBytes > FatSectors * BytesPerSector || fatBytes > MaxFatBytes)
        {
            throw new InvalidDataException("The partition's FAT file system is damaged or larger than DDT reads.");
        }

        if (Type == FatType.Fat32 && RootEntries != 0)
        {
            throw new InvalidDataException("The partition's FAT file system is damaged.");
        }
    }
}
