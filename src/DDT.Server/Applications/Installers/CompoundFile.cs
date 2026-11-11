// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;

namespace DDT.Server.Applications.Installers;

// Reads the streams at the top of an OLE compound file ([MS-CFB]), the container an MSI is stored in. Anything
// damaged throws InvalidDataException.
internal sealed class CompoundFile
{
    // Real MSIs have a few thousand entries and table streams of a few megabytes. The caps keep a forged file from
    // filling memory.
    public const int MaxStreamLength = 64 * 1024 * 1024;
    private const int MaxDirectoryEntries = 128 * 1024;
    private const int MaxMiniFatLength = 16 * 1024 * 1024;
    private const int EntryLength = 128;

    private readonly CompoundFileSectors _sectors;
    private readonly CompoundFileEntry _root;
    private uint[]? _miniFat;
    private List<uint>? _miniStream;

    private CompoundFile(CompoundFileSectors sectors, CompoundFileEntry root, IReadOnlyList<CompoundFileEntry> streams)
    {
        _sectors = sectors;
        _root = root;
        Streams = streams;
    }

    // The streams that are direct children of the root storage.
    public IReadOnlyList<CompoundFileEntry> Streams { get; }

    public static CompoundFile Open(Stream file)
    {
        byte[] start = new byte[CompoundFileHeader.Length];
        file.ReadExactlyAt(0, start);
        CompoundFileHeader header = CompoundFileHeader.Parse(start);
        CompoundFileSectors sectors = CompoundFileSectors.Open(file, header);

        List<CompoundFileEntry> entries = ReadDirectory(sectors);

        if (entries.Count == 0 || entries[0].Type != CompoundFileEntry.RootType)
        {
            throw new InvalidDataException("The directory has no root.");
        }

        return new CompoundFile(sectors, entries[0], RootStreams(entries));
    }

    public byte[] Read(CompoundFileEntry entry)
    {
        if (entry.Size > MaxStreamLength)
        {
            throw new InvalidDataException("A stream is too large.");
        }

        return entry.Size < CompoundFileHeader.MiniStreamCutoff ? ReadMini(entry) : ReadRegular(entry.StartSector, (int)entry.Size);
    }

    private static List<CompoundFileEntry> ReadDirectory(CompoundFileSectors sectors)
    {
        int perSector = sectors.SectorSize / EntryLength;
        List<uint> chain = sectors.OpenChain(sectors.Header.FirstDirectorySector, MaxDirectoryEntries / perSector);
        List<CompoundFileEntry> entries = new(chain.Count * perSector);

        foreach (uint sector in chain)
        {
            byte[] data = sectors.Read(sector);

            for (int index = 0; index < perSector; index++)
            {
                entries.Add(ReadEntry(data.AsSpan(index * EntryLength, EntryLength), sectors.Header.HasLongStreamSizes));
            }
        }

        return entries;
    }

    private static CompoundFileEntry ReadEntry(ReadOnlySpan<byte> entry, bool hasLongSizes)
    {
        int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(entry[0x40..]);
        string name = nameLength is >= 2 and <= 64 && nameLength % 2 == 0
            ? Encoding.Unicode.GetString(entry[..(nameLength - 2)])
            : "";
        ulong size = BinaryPrimitives.ReadUInt64LittleEndian(entry[0x78..]);

        // Version 3 writers may leave garbage in the high half of the size.
        if (!hasLongSizes)
        {
            size &= uint.MaxValue;
        }

        return new CompoundFileEntry(
            name,
            entry[0x42],
            Left: BinaryPrimitives.ReadUInt32LittleEndian(entry[0x44..]),
            Right: BinaryPrimitives.ReadUInt32LittleEndian(entry[0x48..]),
            Child: BinaryPrimitives.ReadUInt32LittleEndian(entry[0x4C..]),
            StartSector: BinaryPrimitives.ReadUInt32LittleEndian(entry[0x74..]),
            Size: size > long.MaxValue ? long.MaxValue : (long)size);
    }

    // The root's children form a red-black tree; the walk ignores the colours and refuses a cycle.
    private static List<CompoundFileEntry> RootStreams(List<CompoundFileEntry> entries)
    {
        List<CompoundFileEntry> streams = [];
        bool[] seen = new bool[entries.Count];
        Stack<uint> pending = new([entries[0].Child]);

        while (pending.TryPop(out uint id))
        {
            if (id == CompoundFileSectors.NoEntry)
            {
                continue;
            }

            if (id >= entries.Count || seen[id])
            {
                throw new InvalidDataException("The directory tree is damaged.");
            }

            seen[id] = true;
            CompoundFileEntry entry = entries[(int)id];

            if (entry.Type == CompoundFileEntry.StreamType)
            {
                streams.Add(entry);
            }

            pending.Push(entry.Left);
            pending.Push(entry.Right);
        }

        return streams;
    }

    private byte[] ReadRegular(uint start, int length)
    {
        int sectorSize = _sectors.SectorSize;
        List<uint> chain = _sectors.Chain(start, ((long)length + sectorSize - 1) / sectorSize, _sectors.Next);
        byte[] data = new byte[length];

        for (int index = 0; index < chain.Count; index++)
        {
            int offset = index * sectorSize;
            _sectors.Read(chain[index], Math.Min(sectorSize, length - offset)).CopyTo(data.AsSpan(offset));
        }

        return data;
    }

    private byte[] ReadMini(CompoundFileEntry entry)
    {
        const int miniSize = CompoundFileHeader.MiniSectorSize;
        int length = (int)entry.Size;
        uint[] miniFat = _miniFat ??= ReadMiniFat();
        List<uint> miniStream = _miniStream ??= MiniStreamSectors();
        List<uint> chain = _sectors.Chain(entry.StartSector, (length + miniSize - 1) / miniSize, sector => sector < miniFat.Length ? miniFat[sector] : CompoundFileSectors.NoEntry);
        byte[] data = new byte[length];
        int perSector = _sectors.SectorSize / miniSize;

        for (int index = 0; index < chain.Count; index++)
        {
            uint mini = chain[index];

            if (mini / perSector >= miniStream.Count)
            {
                throw new InvalidDataException("A mini sector lies outside the mini stream.");
            }

            int offset = index * miniSize;
            int within = (int)(mini % perSector) * miniSize;
            int count = Math.Min(miniSize, length - offset);
            byte[] sector = _sectors.Read(miniStream[(int)(mini / perSector)], within + count);
            sector.AsSpan(within, count).CopyTo(data.AsSpan(offset));
        }

        return data;
    }

    private uint[] ReadMiniFat()
    {
        long length = (long)_sectors.Header.MiniFatSectorCount * _sectors.SectorSize;

        if (length > MaxMiniFatLength)
        {
            throw new InvalidDataException("The mini FAT is too large.");
        }

        byte[] data = ReadRegular(_sectors.Header.FirstMiniFatSector, (int)length);
        uint[] miniFat = new uint[data.Length / 4];

        for (int index = 0; index < miniFat.Length; index++)
        {
            miniFat[index] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(index * 4));
        }

        return miniFat;
    }

    // The root entry's chain holds the mini stream, where streams under the cutoff live in 64-byte sectors.
    private List<uint> MiniStreamSectors()
    {
        long count = (_root.Size + _sectors.SectorSize - 1) / _sectors.SectorSize;

        if (count > _sectors.SectorCount)
        {
            throw new InvalidDataException("The mini stream is larger than the file.");
        }

        return _sectors.Chain(_root.StartSector, count, _sectors.Next);
    }
}
