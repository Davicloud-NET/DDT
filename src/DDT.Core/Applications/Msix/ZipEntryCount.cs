// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;

namespace DDT.Core.Applications.Msix;

// Reads how many entries a zip's end record claims, before ZipArchive loads them all into memory.
internal static class ZipEntryCount
{
    private const uint EndSignature = 0x06054b50;
    private const uint Zip64LocatorSignature = 0x07064b50;
    private const uint Zip64EndSignature = 0x06064b50;
    private const int EndLength = 22;
    private const int Zip64LocatorLength = 20;
    private const int Zip64EndLength = 56;
    private const int MaxCommentLength = ushort.MaxValue;

    // Null when the stream has no end record that can be read.
    public static ulong? Of(Stream zip)
    {
        if (!zip.CanSeek || zip.Length < EndLength)
        {
            return null;
        }

        byte[] tail = new byte[(int)Math.Min(zip.Length, EndLength + MaxCommentLength)];
        long tailStart = zip.Length - tail.Length;
        zip.Position = tailStart;
        zip.ReadExactly(tail);

        for (int end = tail.Length - EndLength; end >= 0; end--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(end)) != EndSignature)
            {
                continue;
            }

            ushort entries = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(end + 10));

            return entries != ushort.MaxValue ? entries : Zip64Entries(zip, tailStart + end);
        }

        return null;
    }

    private static ulong? Zip64Entries(Stream zip, long end)
    {
        if (end < Zip64LocatorLength)
        {
            return null;
        }

        byte[] locator = new byte[Zip64LocatorLength];
        zip.Position = end - Zip64LocatorLength;
        zip.ReadExactly(locator);

        if (BinaryPrimitives.ReadUInt32LittleEndian(locator) != Zip64LocatorSignature)
        {
            return null;
        }

        ulong offset = BinaryPrimitives.ReadUInt64LittleEndian(locator.AsSpan(8));

        if (zip.Length < Zip64EndLength || offset > (ulong)(zip.Length - Zip64EndLength))
        {
            return null;
        }

        byte[] record = new byte[Zip64EndLength];
        zip.Position = (long)offset;
        zip.ReadExactly(record);

        return BinaryPrimitives.ReadUInt32LittleEndian(record) == Zip64EndSignature
            ? BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(32))
            : null;
    }
}
