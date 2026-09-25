// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;

namespace DDT.Core.Disks;

// Lays out a FAT volume of sizeBytes in memory, with its files one after another, as Microsoft's FAT specification
// (version 1.03) defines it. Without a type it is FAT16, or FAT12 when the volume is too small for FAT16. The label goes
// into the boot sector and the root directory, where Linux's blkid reads it. hiddenSectors is the first sector of the
// partition the volume goes into.
public sealed class FatVolumeBuilder(long sizeBytes, string label, uint serialNumber, DateTime timestamp)
{
    public const int SectorSize = 512;

    private const int DirectoryEntryBytes = 32;
    private const int RootEntries = 512;
    private const byte Media = 0xF8;

    // Drivers disagree about volumes whose cluster count is near a type's limit, so none is made there.
    private const int Margin = 16;

    private readonly FatBuildNode _root = new("", null);

    public FatType? Type { get; init; }

    public long HiddenSectors { get; init; }

    // Adds a file, and the directories on its path that are not there yet. path separates them with \ or /.
    public void AddFile(string path, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(content);

        string[] parts = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            throw new ArgumentException("The path names no file.", nameof(path));
        }

        FatBuildNode directory = _root;

        foreach (string part in parts[..^1])
        {
            FatBuildNode? child = Child(directory, part);

            if (child is { IsDirectory: false })
            {
                throw new ArgumentException($"{part} is a file.", nameof(path));
            }

            if (child is null)
            {
                child = new FatBuildNode(part, null);
                directory.Children.Add(child);
            }

            directory = child;
        }

        if (Child(directory, parts[^1]) is not null)
        {
            throw new ArgumentException($"{path} was added before.", nameof(path));
        }

        directory.Children.Add(new FatBuildNode(parts[^1], content));
    }

    public byte[] Build()
    {
        if (label.Length is 0 or > FatNames.ShortNameLength || !label.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '_' or '-'))
        {
            throw new InvalidOperationException($"'{label}' cannot be the label of a FAT volume.");
        }

        FatGeometry geometry = Layout();
        int clusterBytes = geometry.SectorsPerCluster * SectorSize;

        NameEntries(_root);
        uint next = 2;

        if (geometry.Type == FatType.Fat32)
        {
            _root.Clusters = Math.Max(1, ClustersFor(DirectoryBytes(_root, isRoot: true), clusterBytes));
            _root.FirstCluster = next;
            next += (uint)_root.Clusters;
        }
        else if (DirectoryBytes(_root, isRoot: true) > RootEntries * DirectoryEntryBytes)
        {
            throw new InvalidOperationException($"The root directory holds at most {RootEntries} entries.");
        }

        Allocate(_root, clusterBytes, ref next);

        if (next - 2 > geometry.Clusters)
        {
            throw new InvalidOperationException("The files do not fit on the volume.");
        }

        WriteEntries(_root, parentCluster: 0, isRoot: true, clusterBytes);

        byte[] volume = new byte[geometry.TotalSectors * SectorSize];
        WriteBootSector(volume, geometry, next);

        long fatOffset = geometry.Reserved * SectorSize;
        byte[] fat = BuildFat(geometry, _root);

        for (int copy = 0; copy < 2; copy++)
        {
            fat.CopyTo(volume.AsSpan((int)(fatOffset + (copy * geometry.FatSectors * SectorSize))));
        }

        long rootOffset = fatOffset + (2 * geometry.FatSectors * SectorSize);
        long dataOffset = rootOffset + (geometry.RootSectors * SectorSize);

        if (geometry.Type != FatType.Fat32)
        {
            _root.Entries.CopyTo(volume.AsSpan((int)rootOffset));
        }

        WriteData(volume, _root, dataOffset, clusterBytes);

        return volume;
    }

    private static FatBuildNode? Child(FatBuildNode directory, string name) =>
        directory.Children.FirstOrDefault(child => string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase));

    private FatGeometry Layout()
    {
        long totalSectors = sizeBytes / SectorSize;

        if (Type is { } type)
        {
            return Fitting(type, totalSectors)
                ?? throw new InvalidOperationException($"A volume of {sizeBytes} bytes cannot be {type}.");
        }

        return Fitting(FatType.Fat16, totalSectors)
            ?? Fitting(FatType.Fat12, totalSectors)
            ?? throw new InvalidOperationException($"A volume of {sizeBytes} bytes is too large for FAT16. Ask for FAT32.");
    }

    // The smallest clusters that give a cluster count the type allows.
    private static FatGeometry? Fitting(FatType type, long totalSectors)
    {
        for (int sectorsPerCluster = 1; sectorsPerCluster <= 64; sectorsPerCluster *= 2)
        {
            int reserved = type == FatType.Fat32 ? 32 : 1;
            int rootSectors = type == FatType.Fat32 ? 0 : RootEntries * DirectoryEntryBytes / SectorSize;
            long fatSectors = 1;
            long clusters;

            while (true)
            {
                clusters = (totalSectors - reserved - (2 * fatSectors) - rootSectors) / sectorsPerCluster;

                if (clusters <= 0)
                {
                    return null;
                }

                long bytes = type switch
                {
                    FatType.Fat12 => (((clusters + 2) * 3) + 1) / 2,
                    FatType.Fat16 => (clusters + 2) * 2,
                    _ => (clusters + 2) * 4,
                };
                long needed = (bytes + SectorSize - 1) / SectorSize;

                if (needed <= fatSectors)
                {
                    break;
                }

                fatSectors = needed;
            }

            (long minimum, long maximum) = type switch
            {
                FatType.Fat12 => (1L, FatVolume.MaxFatClusters12 - Margin),
                FatType.Fat16 => (FatVolume.MaxFatClusters12 + 1L + Margin, FatVolume.MaxFatClusters16 - Margin),
                _ => (FatVolume.MaxFatClusters16 + 1L + Margin, 0x0FFFFFF5L),
            };

            // Larger clusters only make fewer of them, which cannot help a type that needs more.
            if (clusters < minimum)
            {
                return null;
            }

            if (clusters <= maximum)
            {
                return new FatGeometry(type, totalSectors, sectorsPerCluster, reserved, rootSectors, fatSectors, (uint)clusters);
            }
        }

        return null;
    }

    private static void NameEntries(FatBuildNode directory)
    {
        HashSet<string> taken = new(StringComparer.Ordinal);

        foreach (FatBuildNode child in directory.Children)
        {
            if (FatNames.AsShortName(child.Name) is { } shortName && taken.Add(Encoding.ASCII.GetString(shortName)))
            {
                child.ShortName = shortName;
                child.HasLongName = false;
            }
            else
            {
                child.ShortName = FatNames.Generate(child.Name, taken);
                child.HasLongName = true;
            }

            if (child.IsDirectory)
            {
                NameEntries(child);
            }
        }
    }

    private static int DirectoryBytes(FatBuildNode directory, bool isRoot)
    {
        int entries = isRoot ? 1 : 2;

        foreach (FatBuildNode child in directory.Children)
        {
            entries += 1 + (child.HasLongName ? LongNameEntries(child.Name) : 0);
        }

        return entries * DirectoryEntryBytes;
    }

    private static int LongNameEntries(string name) => (name.Length + FatNames.LongNameCharacters - 1) / FatNames.LongNameCharacters;

    private static int ClustersFor(long bytes, int clusterBytes) => (int)((bytes + clusterBytes - 1) / clusterBytes);

    private static void Allocate(FatBuildNode directory, int clusterBytes, ref uint next)
    {
        foreach (FatBuildNode child in directory.Children)
        {
            child.Clusters = child.IsDirectory
                ? Math.Max(1, ClustersFor(DirectoryBytes(child, isRoot: false), clusterBytes))
                : ClustersFor(child.Content!.Length, clusterBytes);
            child.FirstCluster = child.Clusters == 0 ? 0 : next;
            next += (uint)child.Clusters;
        }

        foreach (FatBuildNode child in directory.Children.Where(child => child.IsDirectory))
        {
            Allocate(child, clusterBytes, ref next);
        }
    }

    private void WriteEntries(FatBuildNode directory, uint parentCluster, bool isRoot, int clusterBytes)
    {
        int size = isRoot && directory.Clusters == 0 ? RootEntries * DirectoryEntryBytes : directory.Clusters * clusterBytes;
        byte[] entries = new byte[size];
        int offset = 0;

        if (isRoot)
        {
            WriteShortEntry(entries.AsSpan(offset), Encoding.ASCII.GetBytes(label.ToUpperInvariant().PadRight(FatNames.ShortNameLength)), 0x08, 0, 0);
            offset += DirectoryEntryBytes;
        }
        else
        {
            WriteShortEntry(entries.AsSpan(offset), Encoding.ASCII.GetBytes(".".PadRight(FatNames.ShortNameLength)), 0x10, directory.FirstCluster, 0);
            WriteShortEntry(entries.AsSpan(offset + DirectoryEntryBytes), Encoding.ASCII.GetBytes("..".PadRight(FatNames.ShortNameLength)), 0x10, parentCluster, 0);
            offset += 2 * DirectoryEntryBytes;
        }

        foreach (FatBuildNode child in directory.Children)
        {
            if (child.HasLongName)
            {
                offset = WriteLongName(entries, offset, child.Name, FatNames.Checksum(child.ShortName));
            }

            WriteShortEntry(
                entries.AsSpan(offset),
                child.ShortName,
                child.IsDirectory ? (byte)0x10 : (byte)0x20,
                child.FirstCluster,
                child.IsDirectory ? 0 : (uint)child.Content!.Length);
            offset += DirectoryEntryBytes;
        }

        directory.Entries = entries;

        foreach (FatBuildNode child in directory.Children.Where(child => child.IsDirectory))
        {
            WriteEntries(child, isRoot ? 0 : directory.FirstCluster, isRoot: false, clusterBytes);
        }
    }

    // The long name's parts go in reverse, the last part first and marked with 0x40.
    private static int WriteLongName(byte[] entries, int offset, string name, byte checksum)
    {
        int count = LongNameEntries(name);

        for (int order = count; order >= 1; order--)
        {
            Span<byte> entry = entries.AsSpan(offset, DirectoryEntryBytes);
            entry[0] = (byte)(order | (order == count ? 0x40 : 0));
            entry[11] = 0x0F;
            entry[13] = checksum;
            int index = 0;

            foreach ((int start, int length) in new[] { (1, 5), (14, 6), (28, 2) })
            {
                for (int character = 0; character < length; character++)
                {
                    int position = ((order - 1) * FatNames.LongNameCharacters) + index++;
                    ushort value = position < name.Length ? name[position] : position == name.Length ? (ushort)0 : (ushort)0xFFFF;
                    BinaryPrimitives.WriteUInt16LittleEndian(entry[(start + (character * 2))..], value);
                }
            }

            offset += DirectoryEntryBytes;
        }

        return offset;
    }

    private void WriteShortEntry(Span<byte> entry, byte[] shortName, byte attributes, uint cluster, uint size)
    {
        shortName.CopyTo(entry);
        entry[11] = attributes;
        (ushort date, ushort time) = FatTimestamp();
        BinaryPrimitives.WriteUInt16LittleEndian(entry[14..], time);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[16..], date);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[18..], date);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[20..], (ushort)(cluster >> 16));
        BinaryPrimitives.WriteUInt16LittleEndian(entry[22..], time);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[24..], date);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[26..], (ushort)cluster);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[28..], size);
    }

    private (ushort Date, ushort Time) FatTimestamp()
    {
        DateTime at = timestamp.Year < 1980 ? new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc) : timestamp;

        return (
            (ushort)(((at.Year - 1980) << 9) | (at.Month << 5) | at.Day),
            (ushort)((at.Hour << 11) | (at.Minute << 5) | (at.Second / 2)));
    }

    private void WriteBootSector(byte[] volume, FatGeometry geometry, uint nextFree)
    {
        Span<byte> boot = volume.AsSpan(0, SectorSize);
        bool fat32 = geometry.Type == FatType.Fat32;
        boot[0] = 0xEB;
        boot[1] = fat32 ? (byte)0x58 : (byte)0x3C;
        boot[2] = 0x90;
        "MSWIN4.1"u8.CopyTo(boot[3..]);
        BinaryPrimitives.WriteUInt16LittleEndian(boot[11..], SectorSize);
        boot[13] = (byte)geometry.SectorsPerCluster;
        BinaryPrimitives.WriteUInt16LittleEndian(boot[14..], (ushort)geometry.Reserved);
        boot[16] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(boot[17..], fat32 ? (ushort)0 : (ushort)RootEntries);

        if (geometry.TotalSectors < 0x10000 && !fat32)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(boot[19..], (ushort)geometry.TotalSectors);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(boot[32..], (uint)geometry.TotalSectors);
        }

        boot[21] = Media;
        BinaryPrimitives.WriteUInt16LittleEndian(boot[24..], 63);
        BinaryPrimitives.WriteUInt16LittleEndian(boot[26..], 255);
        BinaryPrimitives.WriteUInt32LittleEndian(boot[28..], (uint)HiddenSectors);

        int extended = 36;

        if (fat32)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(boot[36..], (uint)geometry.FatSectors);
            BinaryPrimitives.WriteUInt32LittleEndian(boot[44..], _root.FirstCluster);
            BinaryPrimitives.WriteUInt16LittleEndian(boot[48..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(boot[50..], 6);
            extended = 64;
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(boot[22..], (ushort)geometry.FatSectors);
        }

        boot[extended] = 0x80;
        boot[extended + 2] = 0x29;
        BinaryPrimitives.WriteUInt32LittleEndian(boot[(extended + 3)..], serialNumber);
        Encoding.ASCII.GetBytes(label.ToUpperInvariant().PadRight(FatNames.ShortNameLength), boot[(extended + 7)..]);
        Encoding.ASCII.GetBytes(geometry.Type switch { FatType.Fat12 => "FAT12   ", FatType.Fat16 => "FAT16   ", _ => "FAT32   " }, boot[(extended + 18)..]);
        boot[510] = 0x55;
        boot[511] = 0xAA;

        if (fat32)
        {
            Span<byte> info = volume.AsSpan(SectorSize, SectorSize);
            BinaryPrimitives.WriteUInt32LittleEndian(info, 0x41615252);
            BinaryPrimitives.WriteUInt32LittleEndian(info[484..], 0x61417272);
            BinaryPrimitives.WriteUInt32LittleEndian(info[488..], geometry.Clusters - (nextFree - 2));
            BinaryPrimitives.WriteUInt32LittleEndian(info[492..], nextFree);
            BinaryPrimitives.WriteUInt32LittleEndian(info[508..], 0xAA550000);
            volume.AsSpan(0, 3 * SectorSize).CopyTo(volume.AsSpan(6 * SectorSize));
        }
    }

    private static byte[] BuildFat(FatGeometry geometry, FatBuildNode root)
    {
        byte[] fat = new byte[geometry.FatSectors * SectorSize];
        Set(fat, geometry.Type, 0, geometry.Type switch { FatType.Fat12 => 0xF00u | Media, FatType.Fat16 => 0xFF00u | Media, _ => 0x0FFFFF00u | Media });
        Set(fat, geometry.Type, 1, End(geometry.Type));

        void Chain(FatBuildNode node)
        {
            for (int index = 0; index < node.Clusters; index++)
            {
                uint cluster = node.FirstCluster + (uint)index;
                Set(fat, geometry.Type, cluster, index == node.Clusters - 1 ? End(geometry.Type) : cluster + 1);
            }

            foreach (FatBuildNode child in node.Children)
            {
                Chain(child);
            }
        }

        Chain(root);

        return fat;
    }

    private static uint End(FatType type) => type switch
    {
        FatType.Fat12 => 0xFFF,
        FatType.Fat16 => 0xFFFF,
        _ => 0x0FFFFFFF,
    };

    private static void Set(byte[] fat, FatType type, uint cluster, uint value)
    {
        switch (type)
        {
            case FatType.Fat12:
                Span<byte> pair = fat.AsSpan((int)(cluster + (cluster / 2)), 2);
                uint word = BinaryPrimitives.ReadUInt16LittleEndian(pair);
                word = (cluster & 1) == 0 ? (word & 0xF000) | value : (word & 0x000F) | (value << 4);
                BinaryPrimitives.WriteUInt16LittleEndian(pair, (ushort)word);
                break;
            case FatType.Fat16:
                BinaryPrimitives.WriteUInt16LittleEndian(fat.AsSpan((int)(cluster * 2)), (ushort)value);
                break;
            default:
                BinaryPrimitives.WriteUInt32LittleEndian(fat.AsSpan((int)(cluster * 4)), value);
                break;
        }
    }

    private static void WriteData(byte[] volume, FatBuildNode directory, long dataOffset, int clusterBytes)
    {
        if (directory.Clusters > 0)
        {
            directory.Entries.CopyTo(volume.AsSpan((int)(dataOffset + ((directory.FirstCluster - 2L) * clusterBytes))));
        }

        foreach (FatBuildNode child in directory.Children)
        {
            if (child.IsDirectory)
            {
                WriteData(volume, child, dataOffset, clusterBytes);
            }
            else if (child.Clusters > 0)
            {
                child.Content!.CopyTo(volume.AsSpan((int)(dataOffset + ((child.FirstCluster - 2L) * clusterBytes))));
            }
        }
    }
}
