// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;

namespace DDT.Agent.Deployment;

// Reads DRIVE_LAYOUT_INFORMATION_EX as IOCTL_DISK_GET_DRIVE_LAYOUT_EX returns it (winioctl.h, 64-bit layout). Each
// entry is a PARTITION_INFORMATION_EX.
public static class DriveLayoutReader
{
    public const int HeaderLength = 48;
    public const int EntryLength = 144;

    public const int StyleMbr = 0;
    public const int StyleGpt = 1;
    public const int StyleRaw = 2;

    public static readonly Guid EfiSystemPartitionType = Guid.Parse("c12a7328-f81f-11d2-ba4b-00a0c93ec93b");

    private const int PartitionCountOffset = 4;
    private const int EntryPartitionNumberOffset = 24;
    private const int EntryMbrTypeOffset = 32;
    private const int EntryGptTypeOffset = 32;
    private const int EntryGptIdOffset = 48;
    private const int GuidLength = 16;

    // An MBR layout always lists four slots per table, and the extended container holding logical drives is no
    // partition anyone would recognise, so both are left out.
    public static int CountUsedPartitions(ReadOnlySpan<byte> layout)
    {
        int count = EntryCount(layout, out int style);

        if (style is not (StyleMbr or StyleGpt))
        {
            return 0;
        }

        int used = 0;

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> entry = Entry(layout, index);

            if (style == StyleGpt)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(entry[EntryPartitionNumberOffset..]) != 0)
                {
                    used++;
                }

                continue;
            }

            if (entry[EntryMbrTypeOffset] is not (0x00 or 0x05 or 0x0F or 0x85))
            {
                used++;
            }
        }

        return used;
    }

    // The unique partition GUIDs of the disk's EFI system partitions, which firmware boot entries name.
    public static IReadOnlyList<Guid> EfiSystemPartitionIds(ReadOnlySpan<byte> layout)
    {
        int count = EntryCount(layout, out int style);
        List<Guid> ids = [];

        if (style != StyleGpt)
        {
            return ids;
        }

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> entry = Entry(layout, index);
            Guid id = new(entry.Slice(EntryGptIdOffset, GuidLength));

            if (new Guid(entry.Slice(EntryGptTypeOffset, GuidLength)) == EfiSystemPartitionType && !ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    // The number of the GPT partition whose unique GUID is id, which diskpart's select partition takes, or null when
    // the disk has none.
    public static uint? PartitionNumberOf(ReadOnlySpan<byte> layout, Guid id)
    {
        int count = EntryCount(layout, out int style);

        if (style != StyleGpt)
        {
            return null;
        }

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> entry = Entry(layout, index);
            uint number = BinaryPrimitives.ReadUInt32LittleEndian(entry[EntryPartitionNumberOffset..]);

            if (number != 0 && new Guid(entry.Slice(EntryGptIdOffset, GuidLength)) == id)
            {
                return number;
            }
        }

        return null;
    }

    private static int EntryCount(ReadOnlySpan<byte> layout, out int style)
    {
        if (layout.Length < HeaderLength)
        {
            throw new ArgumentException("The drive layout is too short.", nameof(layout));
        }

        style = BinaryPrimitives.ReadInt32LittleEndian(layout);
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(layout[PartitionCountOffset..]);

        if (style is not (StyleMbr or StyleGpt))
        {
            return 0;
        }

        if (count > (layout.Length - HeaderLength) / EntryLength)
        {
            throw new ArgumentException("The drive layout lists more partitions than it holds.", nameof(layout));
        }

        return (int)count;
    }

    private static ReadOnlySpan<byte> Entry(ReadOnlySpan<byte> layout, int index) => layout.Slice(HeaderLength + (index * EntryLength), EntryLength);
}
