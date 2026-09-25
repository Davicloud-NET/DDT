// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace DDT.Core.Disks;

// Reads a FAT12, FAT16 or FAT32 volume, such as the EFI system partition of a disk image, from a stream at an offset,
// as Microsoft's FAT specification (version 1.03) lays it out. The volume comes from an uploaded file and may hold
// anything, so every size is capped and every cluster chain checked.
public sealed class FatVolume
{
    public const int MaxFatClusters12 = 4084;
    public const int MaxFatClusters16 = 65524;

    private const int MaxFatBytes = 16 * 1024 * 1024;
    private const int MaxDirectoryBytes = 65536 * DirectoryEntryBytes;
    private const int DirectoryEntryBytes = 32;
    private const byte LongNameAttributes = 0x0F;
    private const byte VolumeIdAttribute = 0x08;
    private const byte DirectoryAttribute = 0x10;

    private readonly Stream _stream;
    private readonly long _offset;
    private readonly byte[] _fat;
    private readonly long _dataOffset;
    private readonly long _rootOffset;
    private readonly int _rootBytes;
    private readonly uint _rootCluster;
    private readonly uint _clusterCount;

    private FatVolume(
        Stream stream,
        long offset,
        FatType type,
        int clusterBytes,
        uint clusterCount,
        byte[] fat,
        long rootOffset,
        int rootBytes,
        uint rootCluster,
        long dataOffset)
    {
        _stream = stream;
        _offset = offset;
        Type = type;
        ClusterBytes = clusterBytes;
        _clusterCount = clusterCount;
        _fat = fat;
        _rootOffset = rootOffset;
        _rootBytes = rootBytes;
        _rootCluster = rootCluster;
        _dataOffset = dataOffset;
    }

    public FatType Type { get; }

    public int ClusterBytes { get; }

    // From the root directory's volume label entry, else from the boot sector; null when the volume has none.
    public string? Label { get; private set; }

    // Throws InvalidDataException when the volume at offset, length bytes long, is no FAT volume DDT can read.
    public static FatVolume Open(Stream stream, long offset, long length)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] boot = ReadExactly(stream, offset, 512, "boot sector");

        if (boot[510] != 0x55 || boot[511] != 0xAA)
        {
            throw new InvalidDataException("The partition holds no FAT file system.");
        }

        int bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(11));
        int sectorsPerCluster = boot[13];
        int reserved = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(14));
        int fats = boot[16];
        int rootEntries = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(17));
        long totalSectors = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(19)) is ushort small and not 0
            ? small
            : BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(32));
        long fatSectors = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(22)) is ushort fat16 and not 0
            ? fat16
            : BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(36));

        if (bytesPerSector is not (512 or 1024 or 2048 or 4096)
            || sectorsPerCluster is 0 or > 128
            || !int.IsPow2(sectorsPerCluster)
            || reserved == 0
            || fats == 0
            || fatSectors == 0
            || totalSectors * bytesPerSector > length)
        {
            throw new InvalidDataException("The partition holds no FAT file system DDT can read.");
        }

        int rootSectors = ((rootEntries * DirectoryEntryBytes) + bytesPerSector - 1) / bytesPerSector;
        long firstData = reserved + (fats * fatSectors) + rootSectors;

        if (firstData >= totalSectors)
        {
            throw new InvalidDataException("The partition's FAT file system is damaged.");
        }

        uint clusterCount = (uint)((totalSectors - firstData) / sectorsPerCluster);
        FatType type = clusterCount <= MaxFatClusters12 ? FatType.Fat12 : clusterCount <= MaxFatClusters16 ? FatType.Fat16 : FatType.Fat32;
        long fatBytesNeeded = type switch
        {
            FatType.Fat12 => ((clusterCount + 2) * 3 + 1) / 2,
            FatType.Fat16 => (clusterCount + 2) * 2,
            _ => (clusterCount + 2) * 4,
        };

        if (fatBytesNeeded > fatSectors * bytesPerSector || fatBytesNeeded > MaxFatBytes)
        {
            throw new InvalidDataException("The partition's FAT file system is damaged or larger than DDT reads.");
        }

        if (type == FatType.Fat32 && rootEntries != 0)
        {
            throw new InvalidDataException("The partition's FAT file system is damaged.");
        }

        byte[] fat = ReadExactly(stream, offset + ((long)reserved * bytesPerSector), (int)fatBytesNeeded, "allocation table");
        FatVolume volume = new(
            stream,
            offset,
            type,
            sectorsPerCluster * bytesPerSector,
            clusterCount,
            fat,
            offset + ((reserved + (fats * fatSectors)) * bytesPerSector),
            rootSectors * bytesPerSector,
            type == FatType.Fat32 ? BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(44)) : 0,
            offset + (firstData * bytesPerSector));

        int labelOffset = type == FatType.Fat32 ? 71 : 43;
        int signatureOffset = type == FatType.Fat32 ? 66 : 38;
        string? bootLabel = boot[signatureOffset] == 0x29 ? Encoding.ASCII.GetString(boot, labelOffset, 11).TrimEnd(' ') : null;
        volume.Label = volume.ReadRootLabel() ?? (bootLabel is null or "" or "NO NAME" ? null : bootLabel);

        return volume;
    }

    // The entries of the directory at path, such as "EFI\BOOT"; "" is the root.
    public IReadOnlyList<FatEntry> List(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string[] parts = Split(path);

        if (parts.Length == 0)
        {
            return ReadEntries(RootDirectory()).Entries;
        }

        FatEntry directory = Find(path) ?? throw new DirectoryNotFoundException($"{path} is not on the volume.");

        return directory.IsDirectory
            ? ReadEntries(Chain(directory.FirstCluster, MaxDirectoryBytes)).Entries
            : throw new DirectoryNotFoundException($"{path} is a file.");
    }

    // The entry at path, compared without regard to case; null when there is none.
    public FatEntry? Find(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string[] parts = Split(path);
        IReadOnlyList<FatEntry> entries = ReadEntries(RootDirectory()).Entries;
        FatEntry? found = null;

        for (int index = 0; index < parts.Length; index++)
        {
            found = entries.FirstOrDefault(entry => string.Equals(entry.Name, parts[index], StringComparison.OrdinalIgnoreCase));

            if (found is null || index == parts.Length - 1)
            {
                break;
            }

            if (!found.IsDirectory)
            {
                return null;
            }

            entries = ReadEntries(Chain(found.FirstCluster, MaxDirectoryBytes)).Entries;
        }

        return parts.Length == 0 ? null : found;
    }

    public byte[] ReadFile(FatEntry file, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.IsDirectory)
        {
            throw new ArgumentException($"{file.Name} is a directory.", nameof(file));
        }

        if (file.Size > maxBytes)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"{file.Name} holds {file.Size} bytes, more than the {maxBytes} DDT reads."));
        }

        if (file.Size == 0)
        {
            return [];
        }

        byte[] content = Chain(file.FirstCluster, (int)file.Size);

        return content.Length < file.Size
            ? throw new InvalidDataException($"{file.Name} is shorter on the volume than its directory entry says.")
            : content[..(int)file.Size];
    }

    private string? ReadRootLabel() => ReadEntries(RootDirectory()).Label;

    private byte[] RootDirectory() =>
        Type == FatType.Fat32 ? Chain(_rootCluster, MaxDirectoryBytes) : ReadExactly(_stream, _rootOffset, _rootBytes, "root directory");

    // The clusters of the chain that starts at first, up to maxBytes of them.
    private byte[] Chain(uint first, int maxBytes)
    {
        using MemoryStream content = new();
        uint cluster = first;
        HashSet<uint> visited = [];

        while (content.Length < maxBytes)
        {
            if (cluster < 2 || cluster >= _clusterCount + 2)
            {
                throw new InvalidDataException("A cluster chain on the partition's FAT file system is damaged.");
            }

            if (!visited.Add(cluster))
            {
                throw new InvalidDataException("A cluster chain on the partition's FAT file system loops.");
            }

            content.Write(ReadExactly(_stream, _dataOffset + ((long)(cluster - 2) * ClusterBytes), ClusterBytes, "cluster"));
            uint next = Next(cluster);

            if (IsEnd(next))
            {
                break;
            }

            cluster = next;
        }

        return content.ToArray();
    }

    private uint Next(uint cluster) => Type switch
    {
        FatType.Fat12 => (cluster & 1) == 0
            ? (uint)(BinaryPrimitives.ReadUInt16LittleEndian(_fat.AsSpan((int)(cluster + (cluster / 2)))) & 0x0FFF)
            : (uint)(BinaryPrimitives.ReadUInt16LittleEndian(_fat.AsSpan((int)(cluster + (cluster / 2)))) >> 4),
        FatType.Fat16 => BinaryPrimitives.ReadUInt16LittleEndian(_fat.AsSpan((int)(cluster * 2))),
        _ => BinaryPrimitives.ReadUInt32LittleEndian(_fat.AsSpan((int)(cluster * 4))) & 0x0FFFFFFF,
    };

    private bool IsEnd(uint value) => Type switch
    {
        FatType.Fat12 => value >= 0xFF8,
        FatType.Fat16 => value >= 0xFFF8,
        _ => value >= 0x0FFFFFF8,
    };

    // Long names are taken only when every part is there and their checksum names the short entry that follows.
    private static (List<FatEntry> Entries, string? Label) ReadEntries(byte[] directory)
    {
        List<FatEntry> entries = [];
        string? label = null;
        char[]? longName = null;
        int expected = 0;
        byte checksum = 0;

        for (int offset = 0; offset + DirectoryEntryBytes <= directory.Length; offset += DirectoryEntryBytes)
        {
            ReadOnlySpan<byte> entry = directory.AsSpan(offset, DirectoryEntryBytes);

            if (entry[0] == 0x00)
            {
                break;
            }

            byte attributes = entry[11];

            if (entry[0] == 0xE5)
            {
                longName = null;

                continue;
            }

            if ((attributes & 0x3F) == LongNameAttributes)
            {
                int order = entry[0] & 0x1F;

                if ((entry[0] & 0x40) != 0 && order > 0)
                {
                    longName = new char[order * FatNames.LongNameCharacters];
                    expected = order;
                    checksum = entry[13];
                }

                if (longName is null || order != expected || entry[13] != checksum)
                {
                    longName = null;

                    continue;
                }

                ReadLongNamePart(entry, longName.AsSpan((order - 1) * FatNames.LongNameCharacters, FatNames.LongNameCharacters));
                expected--;

                continue;
            }

            string? name = longName is not null && expected == 0 && FatNames.Checksum(entry) == checksum
                ? LongName(longName)
                : null;
            longName = null;

            if ((attributes & VolumeIdAttribute) != 0)
            {
                label ??= Encoding.ASCII.GetString(entry[..FatNames.ShortNameLength]).TrimEnd(' ');

                continue;
            }

            name ??= FatNames.Read(entry);

            if (name is "." or "..")
            {
                continue;
            }

            uint cluster = ((uint)BinaryPrimitives.ReadUInt16LittleEndian(entry[20..]) << 16) | BinaryPrimitives.ReadUInt16LittleEndian(entry[26..]);
            entries.Add(new FatEntry(name, (attributes & DirectoryAttribute) != 0, BinaryPrimitives.ReadUInt32LittleEndian(entry[28..]), cluster));
        }

        return (entries, label is "" ? null : label);
    }

    private static void ReadLongNamePart(ReadOnlySpan<byte> entry, Span<char> part)
    {
        int index = 0;

        foreach ((int start, int count) in new[] { (1, 5), (14, 6), (28, 2) })
        {
            for (int character = 0; character < count; character++)
            {
                part[index++] = (char)BinaryPrimitives.ReadUInt16LittleEndian(entry[(start + (character * 2))..]);
            }
        }
    }

    private static string LongName(char[] name)
    {
        int end = Array.IndexOf(name, '\0');

        return new string(name, 0, end < 0 ? name.Length : end);
    }

    private static string[] Split(string path) => path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

    private static byte[] ReadExactly(Stream stream, long offset, int count, string what)
    {
        byte[] buffer = new byte[count];
        stream.Position = offset;

        if (stream.ReadAtLeast(buffer, count, throwOnEndOfStream: false) < count)
        {
            throw new InvalidDataException($"The partition ends inside its FAT file system's {what}.");
        }

        return buffer;
    }
}
