// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;

namespace DDT.Agent.Facts;

// Adds up the memory modules of the SMBIOS structure table, as its Memory Device structures (DSP0134 7.18) size them.
public static class SmbiosMemoryDevices
{
    private const byte MemoryDeviceType = 17;
    private const byte EndOfTableType = 127;
    private const int HeaderLength = 4;
    private const int SizeOffset = 0x0C;
    private const int ExtendedSizeOffset = 0x1C;

    // The size is unknown, or it is in the extended size field because it needs more than 15 bits.
    private const ushort UnknownSize = 0xFFFF;
    private const ushort SeeExtendedSize = 0x7FFF;
    private const ushort InKilobytes = 0x8000;

    private const ulong KilobytesPerMegabyte = 1024;

    // A malformed structure ends the pass, and what was counted before it stays.
    public static ulong TotalKilobytes(ReadOnlySpan<byte> table)
    {
        ulong total = 0;
        int offset = 0;

        while (offset + HeaderLength <= table.Length)
        {
            byte type = table[offset];
            int length = table[offset + 1];

            if (length < HeaderLength || offset + length > table.Length || type == EndOfTableType)
            {
                break;
            }

            if (type == MemoryDeviceType)
            {
                total += Kilobytes(table.Slice(offset, length));
            }

            // The strings follow the structure and end with two zero bytes.
            int end = table[(offset + length)..].IndexOf("\0\0"u8);

            if (end < 0)
            {
                break;
            }

            offset += length + end + 2;
        }

        return total;
    }

    // An empty socket has size 0.
    private static ulong Kilobytes(ReadOnlySpan<byte> device)
    {
        if (device.Length < SizeOffset + 2)
        {
            return 0;
        }

        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(device[SizeOffset..]);

        if (size is 0 or UnknownSize)
        {
            return 0;
        }

        if (size == SeeExtendedSize)
        {
            return device.Length >= ExtendedSizeOffset + 4
                ? (BinaryPrimitives.ReadUInt32LittleEndian(device[ExtendedSizeOffset..]) & 0x7FFFFFFF) * KilobytesPerMegabyte
                : 0;
        }

        return (size & InKilobytes) != 0 ? (ulong)(size & ~InKilobytes) : size * KilobytesPerMegabyte;
    }
}
