// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Packages;

// The CRC-32 a zip stores for each entry, as zlib computes it. The framework keeps its own internal.
internal static class ZipCrc32
{
    private const uint Polynomial = 0xEDB88320;

    private static readonly uint[] s_table = CreateTable();

    // Continues a checksum over more bytes. A new one starts at 0.
    public static uint Append(uint crc, ReadOnlySpan<byte> data)
    {
        crc = ~crc;

        foreach (byte value in data)
        {
            crc = s_table[(byte)(crc ^ value)] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static uint[] CreateTable()
    {
        uint[] table = new uint[256];

        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index;

            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? Polynomial ^ (value >> 1) : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }
}
