// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace DDT.Core.Disks;

// Reads a FAT12, FAT16 or FAT32 volume as Microsoft's FAT specification 1.03 lays it out, such as a disk image's EFI
// system partition. The volume comes from an uploaded file, so every size is capped and every cluster chain checked.
public sealed class FatVolume
{
    public const int MaxFatClusters12 = 4084;
    public const int MaxFatClusters16 = 65524;

    private const int MaxDirectoryBytes = 65536 * DirectoryEntryBytes;
    private const int DirectoryEntryBytes = 32;
    private const byte LongNameAttributes = 0x0F;
    private const byte VolumeIdAttribute = 0x08;
    private const byte DirectoryAttribute = 0x10;

    private readonly Stream _stream;
    private readonly FatTable _fat;
    private readonly long _dataOffset;
    private readonly long _rootOffset;
    private readonly int _rootBytes;
    private readonly uint _rootCluster;
    private readonly long _clusterCount;

    private FatVolume(Stream stream, long offset, FatBootSector boot, FatTable fat)
    {
        _stream = stream;
        Type = boot.Type;
        ClusterBytes = boot.ClusterBytes;
        _clusterCount = boot.ClusterCount;
        _fat = fat;
        _rootOffset = offset + (boot.RootDirectorySector * boot.BytesPerSector);
        _rootBytes = boot.RootSectors * boot.BytesPerSector;
        _rootCluster = boot.RootCluster;
        _dataOffset = offset + (boot.FirstDataSector * boot.BytesPerSector);
    }

    public FatType Type { get; }

    public int ClusterBytes { get; }

    // Read from the root directory's volume label entry, or else from the boot sector. Null when the volume has none.
    public string? Label { get; private set; }

    // Opens the volume at offset, which is length bytes long. Throws InvalidDataException when it isn't a FAT volume
    // DDT can read.
    public static FatVolume Open(Stream stream, long offset, long length)
    {
        ArgumentNullException.ThrowIfNull(stream);

        FatBootSector boot = FatBootSector.Parse(ReadExactly(stream, offset, FatBootSector.Size, "boot sector"), length);
        byte[] fat = ReadExactly(
            stream,
            offset + ((long)boot.ReservedSectors * boot.BytesPerSector),
            (int)FatTable.BytesFor(boot.Type, boot.ClusterCount),
            "allocation table");
        FatVolume volume = new(stream, offset, boot, new FatTable(boot.Type, fat));
        volume.Label = volume.ReadRootLabel() ?? (boot.Label is "" or "NO NAME" ? null : boot.Label);

        return volume;
    }

    // The entries of the directory at path, such as "EFI\BOOT". An empty path is the root.
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

    // The entry at path, ignoring case, or null when there's none.
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

    // Reads the cluster chain that starts at first, up to maxBytes.
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
            uint next = _fat.Get(cluster);

            if (_fat.IsEnd(next))
            {
                break;
            }

            cluster = next;
        }

        return content.ToArray();
    }

    private static (List<FatEntry> Entries, string? Label) ReadEntries(byte[] directory)
    {
        List<FatEntry> entries = [];
        string? label = null;
        FatLongNameParts longName = new();

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
                longName.Clear();

                continue;
            }

            if ((attributes & 0x3F) == LongNameAttributes)
            {
                longName.Add(entry);

                continue;
            }

            string? name = longName.TakeFor(entry);

            if ((attributes & VolumeIdAttribute) != 0)
            {
                label ??= Encoding.ASCII.GetString(entry[..FatNames.ShortNameLength]).TrimEnd(' ');

                continue;
            }

            name ??= FatNames.Read(entry);

            if (name is not ("." or ".."))
            {
                uint cluster = ((uint)BinaryPrimitives.ReadUInt16LittleEndian(entry[20..]) << 16) | BinaryPrimitives.ReadUInt16LittleEndian(entry[26..]);
                entries.Add(new FatEntry(name, (attributes & DirectoryAttribute) != 0, BinaryPrimitives.ReadUInt32LittleEndian(entry[28..]), cluster));
            }
        }

        return (entries, label is "" ? null : label);
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
