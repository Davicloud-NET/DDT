// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using DDT.Contracts.Messages;

namespace DDT.Core.Disks;

// The GPT header (UEFI 2.10, section 5.3.2). GptLayout reads it from an image and writes it at LBA 1 and at the end
// of the disk.
internal static class GptHeader
{
    public const int MinEntrySize = 128;
    public const int MaxEntryCount = 4096;

    private const ulong Signature = 0x5452415020494645;
    private const uint Revision = 0x00010000;
    private const int Size = 92;
    private const int MaxEntrySize = 4096;

    // Real tables put 16 KiB of entries at LBA 2. The caps keep a table within the first mebibyte, which the agent
    // holds back while it writes an image. They also stop a forged header from asking for gigabytes.
    private const long MaxEntriesLba = GptLayout.AlignmentSectors / 2;
    private const long MaxEntryBytes = GptLayout.AlignmentSectors / 2 * GptLayout.SectorSize;

    public static bool HasSignatureAt(ReadOnlySpan<byte> head, int offset) =>
        head.Length >= offset + 8 && BinaryPrimitives.ReadUInt64LittleEndian(head[offset..]) == Signature;

    public static void Check(ReadOnlySpan<byte> header)
    {
        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);

        if (BinaryPrimitives.ReadUInt32LittleEndian(header[8..]) != Revision || headerSize is < Size or > GptLayout.SectorSize)
        {
            throw new InvalidGptException(ServerMessages.GptDamagedHeader.With());
        }

        Span<byte> copy = stackalloc byte[(int)headerSize];
        header[..(int)headerSize].CopyTo(copy);
        BinaryPrimitives.WriteUInt32LittleEndian(copy[16..], 0);

        if (Crc32.Append(0, copy) != BinaryPrimitives.ReadUInt32LittleEndian(header[16..])
            || BinaryPrimitives.ReadUInt64LittleEndian(header[24..]) != 1)
        {
            throw new InvalidGptException(ServerMessages.GptDamaged.With());
        }

        ulong entriesLba = BinaryPrimitives.ReadUInt64LittleEndian(header[72..]);
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(header[80..]);
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(header[84..]);

        if (entriesLba is < 2 or > MaxEntriesLba
            || count is 0 or > MaxEntryCount
            || size is < MinEntrySize or > MaxEntrySize
            || size % 8 != 0
            || (long)count * size > MaxEntryBytes)
        {
            throw new InvalidGptException(ServerMessages.GptDamagedEntries.With());
        }
    }

    // Call Check first. It bounds the entry array.
    public static (long Lba, int Count, int Size) EntryArray(ReadOnlySpan<byte> header) => (
        checked((long)BinaryPrimitives.ReadUInt64LittleEndian(header[72..])),
        (int)BinaryPrimitives.ReadUInt32LittleEndian(header[80..]),
        (int)BinaryPrimitives.ReadUInt32LittleEndian(header[84..]));

    // Call Check first.
    public static GptHeaderFields Read(ReadOnlySpan<byte> header)
    {
        (long entriesLba, int count, int size) = EntryArray(header);

        return new GptHeaderFields(new Guid(header.Slice(56, 16)), ReadLba(header, 40), ReadLba(header, 48), ReadLba(header, 32), entriesLba, count, size);
    }

    public static uint EntriesCrc(ReadOnlySpan<byte> header) => BinaryPrimitives.ReadUInt32LittleEndian(header[88..]);

    public static byte[] Write(GptHeaderFields fields, long myLba, long alternateLba, long entriesLba, uint entriesCrc)
    {
        byte[] sector = new byte[GptLayout.SectorSize];
        Span<byte> header = sector;
        BinaryPrimitives.WriteUInt64LittleEndian(header, Signature);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], Revision);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], Size);
        BinaryPrimitives.WriteInt64LittleEndian(header[24..], myLba);
        BinaryPrimitives.WriteInt64LittleEndian(header[32..], alternateLba);
        BinaryPrimitives.WriteInt64LittleEndian(header[40..], fields.FirstUsableLba);
        BinaryPrimitives.WriteInt64LittleEndian(header[48..], fields.LastUsableLba);
        fields.DiskId.TryWriteBytes(header[56..]);
        BinaryPrimitives.WriteInt64LittleEndian(header[72..], entriesLba);
        BinaryPrimitives.WriteUInt32LittleEndian(header[80..], (uint)fields.EntryCount);
        BinaryPrimitives.WriteUInt32LittleEndian(header[84..], (uint)fields.EntrySize);
        BinaryPrimitives.WriteUInt32LittleEndian(header[88..], entriesCrc);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], Crc32.Append(0, header[..Size]));

        return sector;
    }

    private static long ReadLba(ReadOnlySpan<byte> header, int offset)
    {
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(header[offset..]);

        return value > long.MaxValue / GptLayout.SectorSize ? throw new InvalidGptException(ServerMessages.GptDamaged.With()) : (long)value;
    }
}
