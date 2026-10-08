// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;

namespace DDT.Agent.Deployment;

// Where the partitions of a disk with a master boot record lie, those inside an extended partition among them.
public static class MbrPartitions
{
    private const int EntriesOffset = 446;
    private const int EntryLength = 16;
    private const int Entries = 4;
    private const int SignatureOffset = 510;
    private const ushort Signature = 0xAA55;
    private const byte Empty = 0x00;
    private const byte GptProtective = 0xEE;

    // A table that leads in a circle ends here.
    private const int MostLogical = 128;

    // Start and length in bytes. Empty without such a table, and for the one that only guards a GPT. head is the
    // first sector of the disk or more.
    public static IReadOnlyList<(long Start, long Length)> Read(IRawDisk disk, ReadOnlySpan<byte> head)
    {
        ArgumentNullException.ThrowIfNull(disk);

        List<(long Start, long Length)> found = [];

        foreach ((byte type, long first, long sectors) in EntriesOf(head))
        {
            found.Add((first * disk.SectorSize, sectors * disk.SectorSize));

            if (IsExtended(type))
            {
                AddLogical(disk, first, found);
            }
        }

        return found;
    }

    // Each logical partition has a table of its own in front of it, which names it and the next such table.
    private static void AddLogical(IRawDisk disk, long extended, List<(long Start, long Length)> found)
    {
        byte[] sector = new byte[disk.SectorSize];
        long table = extended;

        for (int count = 0; count < MostLogical && (table + 1) * disk.SectorSize <= disk.Length; count++)
        {
            try
            {
                disk.Read(table * disk.SectorSize, sector);
            }
            catch (DeploymentStepException)
            {
                return;
            }

            long? next = null;

            foreach ((byte type, long first, long sectors) in EntriesOf(sector))
            {
                if (IsExtended(type))
                {
                    next = extended + first;
                }
                else
                {
                    found.Add(((table + first) * disk.SectorSize, sectors * disk.SectorSize));
                }
            }

            if (next is not { } following || following <= table)
            {
                return;
            }

            table = following;
        }
    }

    private static List<(byte Type, long First, long Sectors)> EntriesOf(ReadOnlySpan<byte> sector)
    {
        List<(byte Type, long First, long Sectors)> entries = [];

        if (sector.Length < SignatureOffset + 2 || BinaryPrimitives.ReadUInt16LittleEndian(sector[SignatureOffset..]) != Signature)
        {
            return entries;
        }

        for (int index = 0; index < Entries; index++)
        {
            ReadOnlySpan<byte> entry = sector.Slice(EntriesOffset + (index * EntryLength), EntryLength);
            byte type = entry[4];
            uint first = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            uint sectors = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);

            if (type is not Empty and not GptProtective && first > 0 && sectors > 0)
            {
                entries.Add((type, first, sectors));
            }
        }

        return entries;
    }

    // The kinds of extended partition: addressed by CHS, by LBA, and the one Linux made.
    private static bool IsExtended(byte type) => type is 0x05 or 0x0F or 0x85;
}
