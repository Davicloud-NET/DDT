// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;

namespace DDT.Server.Tests.Applications;

// Writes compound files with streams under the root, laid out as [MS-CFB] describes: FAT sectors first, then the
// directory, the mini FAT, the mini stream and the regular streams. Streams under 4096 bytes go to the mini stream.
internal sealed class TestCompoundFile
{
    public const uint EndOfChain = 0xFFFFFFFE;

    private const uint FreeSector = 0xFFFFFFFF;
    private const uint FatSector = 0xFFFFFFFD;
    private const uint NoEntry = 0xFFFFFFFF;
    private const int Cutoff = 4096;
    private const int MiniSize = 64;

    private readonly List<uint> _fat = [];
    private readonly int _sectorSize;

    private TestCompoundFile(int sectorSize) => _sectorSize = sectorSize;

    public static byte[] Create(IReadOnlyList<(string Name, byte[] Data)> streams, int sectorSize = 512) =>
        new TestCompoundFile(sectorSize).Write(streams);

    // Points a FAT entry somewhere else. The FAT starts at sector 0, right after the header.
    public static void SetFatEntry(byte[] file, int sectorSize, uint sector, uint next) =>
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(sectorSize + ((int)sector * 4)), next);

    // The sector a stream's data starts in, as its directory entry records it.
    public static uint StartSector(byte[] file, int sectorSize, int entry) =>
        BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(DirectoryOffset(file, sectorSize) + (entry * 128) + 0x74));

    public static void SetRightSibling(byte[] file, int sectorSize, int entry, uint sibling) =>
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(DirectoryOffset(file, sectorSize) + (entry * 128) + 0x48), sibling);

    private static int DirectoryOffset(byte[] file, int sectorSize) =>
        (BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(0x30)) + 1) * sectorSize;

    private byte[] Write(IReadOnlyList<(string Name, byte[] Data)> streams)
    {
        static bool IsMini((string Name, byte[] Data) stream) => stream.Data.Length < Cutoff;

        List<byte[]> miniStreams = [.. streams.Where(IsMini).Select(stream => stream.Data)];
        int miniSectors = miniStreams.Sum(data => Sectors(data.Length, MiniSize));
        int directorySectors = Sectors((streams.Count + 1) * 128, _sectorSize);
        int miniFatSectors = Sectors(miniSectors * 4, _sectorSize);
        int miniStreamSectors = Sectors(miniSectors * MiniSize, _sectorSize);
        int dataSectors = streams.Where(stream => !IsMini(stream)).Sum(stream => Sectors(stream.Data.Length, _sectorSize));
        int others = directorySectors + miniFatSectors + miniStreamSectors + dataSectors;
        int fatSectors = 1;

        while (fatSectors * (_sectorSize / 4) < fatSectors + others)
        {
            fatSectors++;
        }

        _fat.AddRange(Enumerable.Repeat(FatSector, fatSectors));
        uint directory = Allocate(directorySectors);
        uint miniFat = Allocate(miniFatSectors);
        uint miniStream = Allocate(miniStreamSectors);

        List<byte> sectors = [];
        byte[] directoryData = new byte[directorySectors * _sectorSize];
        List<uint> miniFatEntries = [];
        List<byte> miniData = [];
        List<byte[]> regularData = [];
        Entry(directoryData, 0, new("Root Entry", 5, streams.Count > 0 ? 1u : NoEntry, NoEntry, miniStreamSectors > 0 ? miniStream : EndOfChain, miniSectors * MiniSize));

        for (int index = 0; index < streams.Count; index++)
        {
            (string name, byte[] data) = streams[index];
            uint right = index + 1 < streams.Count ? (uint)(index + 2) : NoEntry;
            uint start = IsMini(streams[index]) ? AppendMini(miniFatEntries, miniData, data) : Allocate(Sectors(data.Length, _sectorSize));

            if (!IsMini(streams[index]))
            {
                regularData.Add(data);
            }

            Entry(directoryData, index + 1, new(name, 2, NoEntry, right, data.Length == 0 ? EndOfChain : start, data.Length));
        }

        byte[] miniFatData = new byte[miniFatSectors * _sectorSize];
        miniFatData.AsSpan().Fill(0xFF);

        for (int index = 0; index < miniFatEntries.Count; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(miniFatData.AsSpan(index * 4), miniFatEntries[index]);
        }

        return Assemble(fatSectors, directory, (miniFat, miniFatSectors), [directoryData, miniFatData, Pad([.. miniData]), .. regularData.Select(Pad)]);
    }

    private byte[] Assemble(int fatSectors, uint directory, (uint Start, int Count) miniFat, IEnumerable<byte[]> parts)
    {
        while (_fat.Count % (_sectorSize / 4) != 0)
        {
            _fat.Add(FreeSector);
        }

        byte[] header = new byte[_sectorSize];
        Header(header, fatSectors, directory, miniFat);
        byte[] fat = new byte[_fat.Count * 4];

        for (int index = 0; index < _fat.Count; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(fat.AsSpan(index * 4), _fat[index]);
        }

        return [.. header, .. fat, .. parts.SelectMany(part => part)];
    }

    private void Header(byte[] header, int fatSectors, uint directory, (uint Start, int Count) miniFat)
    {
        Span<byte> span = header;
        byte[] signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        signature.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[0x18..], 0x3E);
        BinaryPrimitives.WriteUInt16LittleEndian(span[0x1A..], (ushort)(_sectorSize == 512 ? 3 : 4));
        BinaryPrimitives.WriteUInt16LittleEndian(span[0x1C..], 0xFFFE);
        BinaryPrimitives.WriteUInt16LittleEndian(span[0x1E..], (ushort)(_sectorSize == 512 ? 9 : 12));
        BinaryPrimitives.WriteUInt16LittleEndian(span[0x20..], 6);
        BinaryPrimitives.WriteInt32LittleEndian(span[0x2C..], fatSectors);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x30..], directory);
        BinaryPrimitives.WriteInt32LittleEndian(span[0x38..], Cutoff);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x3C..], miniFat.Count > 0 ? miniFat.Start : EndOfChain);
        BinaryPrimitives.WriteInt32LittleEndian(span[0x40..], miniFat.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x44..], EndOfChain);

        for (int index = 0; index < 109; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[(0x4C + (index * 4))..], index < fatSectors ? (uint)index : FreeSector);
        }
    }

    private uint Allocate(int count)
    {
        uint start = count == 0 ? EndOfChain : (uint)_fat.Count;

        for (int index = 0; index < count; index++)
        {
            _fat.Add(index + 1 < count ? (uint)(_fat.Count + 1) : EndOfChain);
        }

        return start;
    }

    private static uint AppendMini(List<uint> miniFat, List<byte> miniData, byte[] data)
    {
        uint start = (uint)miniFat.Count;
        int count = Sectors(data.Length, MiniSize);

        for (int index = 0; index < count; index++)
        {
            miniFat.Add(index + 1 < count ? (uint)(miniFat.Count + 1) : EndOfChain);
        }

        miniData.AddRange(data);
        miniData.AddRange(new byte[(count * MiniSize) - data.Length]);

        return start;
    }

    private static void Entry(byte[] directory, int index, DirectoryEntry value)
    {
        Span<byte> entry = directory.AsSpan(index * 128, 128);
        byte[] encoded = Encoding.Unicode.GetBytes(value.Name + "\0");
        encoded.CopyTo(entry);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[0x40..], (ushort)encoded.Length);
        entry[0x42] = value.Type;
        entry[0x43] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(entry[0x44..], NoEntry);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[0x48..], value.Right);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[0x4C..], value.Child);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[0x74..], value.Start);
        BinaryPrimitives.WriteInt64LittleEndian(entry[0x78..], value.Size);
    }

    private byte[] Pad(byte[] data) => [.. data, .. new byte[(Sectors(data.Length, _sectorSize) * _sectorSize) - data.Length]];

    private static int Sectors(int length, int size) => (length + size - 1) / size;

    private sealed record DirectoryEntry(string Name, byte Type, uint Child, uint Right, uint Start, long Size);
}
