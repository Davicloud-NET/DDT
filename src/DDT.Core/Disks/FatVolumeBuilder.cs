// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;

namespace DDT.Core.Disks;

// Lays out a FAT volume in memory, with its files one after another. The label goes into the boot sector and into the
// root directory, where Linux's blkid reads it.
public sealed class FatVolumeBuilder(long sizeBytes, string label, uint serialNumber, DateTime timestamp)
{
    public const int SectorSize = 512;

    private const int DirectoryEntryBytes = 32;
    private const int RootEntries = 512;

    // Drivers disagree about volumes with a cluster count near a type's limit, so the builder stays this far away.
    private const int Margin = 16;

    private readonly FatBuildNode _root = new("", null);
    private readonly FatDirectoryWriter _directories = new(label, timestamp);

    // Null picks FAT16, or FAT12 when the volume is too small for FAT16.
    public FatType? Type { get; init; }

    // The first sector of the partition the volume goes into.
    public long HiddenSectors { get; init; }

    // Adds a file and any missing directories on its path. The path separates them with \ or /.
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

        FatBootSector boot = Layout();
        int clusterBytes = boot.ClusterBytes;

        NameEntries(_root);
        uint next = AllocateClusters(boot, clusterBytes);
        WriteEntries(_root, parentCluster: 0, isRoot: true, clusterBytes);

        boot = boot with
        {
            RootCluster = _root.FirstCluster,
            HiddenSectors = HiddenSectors,
            SerialNumber = serialNumber,
            Label = label.ToUpperInvariant(),
        };
        byte[] volume = new byte[boot.TotalSectors * SectorSize];
        WriteReservedSectors(volume, boot, next);
        FatTable fat = BuildFat(boot, _root);

        for (int copy = 0; copy < boot.FatCount; copy++)
        {
            fat.Bytes.CopyTo(volume.AsSpan((int)((boot.ReservedSectors + (copy * boot.FatSectors)) * SectorSize)));
        }

        if (boot.Type != FatType.Fat32)
        {
            _root.Entries.CopyTo(volume.AsSpan((int)(boot.RootDirectorySector * SectorSize)));
        }

        WriteData(volume, _root, boot.FirstDataSector * SectorSize, clusterBytes);

        return volume;
    }

    private static FatBuildNode? Child(FatBuildNode directory, string name) =>
        directory.Children.FirstOrDefault(child => string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase));

    private FatBootSector Layout()
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

    // Picks the smallest cluster size that gives a cluster count the type allows.
    private static FatBootSector? Fitting(FatType type, long totalSectors)
    {
        for (int sectorsPerCluster = 1; sectorsPerCluster <= 64; sectorsPerCluster *= 2)
        {
            int reserved = type == FatType.Fat32 ? 32 : 1;
            int rootEntries = type == FatType.Fat32 ? 0 : RootEntries;
            int rootSectors = rootEntries * DirectoryEntryBytes / SectorSize;
            long fatSectors = 1;
            long clusters;

            while (true)
            {
                clusters = (totalSectors - reserved - (2 * fatSectors) - rootSectors) / sectorsPerCluster;

                if (clusters <= 0)
                {
                    return null;
                }

                long needed = (FatTable.BytesFor(type, clusters) + SectorSize - 1) / SectorSize;

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

            // Larger clusters only mean fewer of them, which can't help when the type needs more.
            if (clusters < minimum)
            {
                return null;
            }

            if (clusters <= maximum)
            {
                return new FatBootSector(type, SectorSize, sectorsPerCluster, reserved, 2, rootEntries, totalSectors, fatSectors);
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
            entries += 1 + (child.HasLongName ? FatNames.LongNameEntries(child.Name) : 0);
        }

        return entries * DirectoryEntryBytes;
    }

    private static int ClustersFor(long bytes, int clusterBytes) => (int)((bytes + clusterBytes - 1) / clusterBytes);

    // Hands out clusters from 2 on. The FAT32 root directory goes first. Then all children of a directory get their
    // clusters before any grandchildren do.
    private uint AllocateClusters(FatBootSector boot, int clusterBytes)
    {
        uint next = 2;

        if (boot.Type == FatType.Fat32)
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

        return next - 2 > boot.ClusterCount ? throw new InvalidOperationException("The files do not fit on the volume.") : next;
    }

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
        directory.Entries = _directories.Write(directory, parentCluster, isRoot, size);

        foreach (FatBuildNode child in directory.Children.Where(child => child.IsDirectory))
        {
            WriteEntries(child, isRoot ? 0 : directory.FirstCluster, isRoot: false, clusterBytes);
        }
    }

    // FAT32 adds an FSInfo sector with the free cluster count. It also keeps a backup of the boot sector and the
    // FSInfo sector from sector 6.
    private static void WriteReservedSectors(byte[] volume, FatBootSector boot, uint nextFree)
    {
        boot.Write(volume.AsSpan(0, SectorSize));

        if (boot.Type != FatType.Fat32)
        {
            return;
        }

        Span<byte> info = volume.AsSpan(SectorSize, SectorSize);
        BinaryPrimitives.WriteUInt32LittleEndian(info, 0x41615252);
        BinaryPrimitives.WriteUInt32LittleEndian(info[484..], 0x61417272);
        BinaryPrimitives.WriteUInt32LittleEndian(info[488..], (uint)(boot.ClusterCount - (nextFree - 2)));
        BinaryPrimitives.WriteUInt32LittleEndian(info[492..], nextFree);
        BinaryPrimitives.WriteUInt32LittleEndian(info[508..], 0xAA550000);
        volume.AsSpan(0, 3 * SectorSize).CopyTo(volume.AsSpan(6 * SectorSize));
    }

    private static FatTable BuildFat(FatBootSector boot, FatBuildNode root)
    {
        FatTable fat = new(boot.Type, new byte[boot.FatSectors * SectorSize]);

        // Entry 0 holds the media byte with all other bits set. Entry 1 holds an end of chain marker.
        fat.Set(0, (fat.EndMarker & ~0xFFu) | FatBootSector.FixedDiskMedia);
        fat.Set(1, fat.EndMarker);

        void Chain(FatBuildNode node)
        {
            for (int index = 0; index < node.Clusters; index++)
            {
                uint cluster = node.FirstCluster + (uint)index;
                fat.Set(cluster, index == node.Clusters - 1 ? fat.EndMarker : cluster + 1);
            }

            foreach (FatBuildNode child in node.Children)
            {
                Chain(child);
            }
        }

        Chain(root);

        return fat;
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
