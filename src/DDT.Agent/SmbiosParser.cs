// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;

namespace DDT.Agent;

// Parses the buffer GetSystemFirmwareTable returns for the 'RSMB' provider: an 8 byte
// RawSMBIOSData header followed by the SMBIOS structure table. Reading it directly avoids needing
// the WinPE-WMI optional component in the boot image.
public static class SmbiosParser
{
    private const int RawHeaderLength = 8;
    private const byte SystemInformationType = 1;
    private const byte EndOfTableType = 127;
    private const int UuidOffset = 0x08;
    private const int UuidLength = 16;

    public static SmbiosSystemInformation? TryReadSystemInformation(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < RawHeaderLength)
        {
            return null;
        }

        byte major = raw[1];
        byte minor = raw[2];
        int tableLength = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(raw[4..]), (uint)(raw.Length - RawHeaderLength));
        ReadOnlySpan<byte> table = raw.Slice(RawHeaderLength, tableLength);

        int offset = 0;

        while (offset + 4 <= table.Length)
        {
            byte type = table[offset];
            byte length = table[offset + 1];

            if (length < 4 || offset + length > table.Length)
            {
                return null;
            }

            ReadOnlySpan<byte> formatted = table.Slice(offset, length);
            int stringsStart = offset + length;
            int end = FindStructureEnd(table, stringsStart);

            if (end < 0)
            {
                return null;
            }

            if (type == SystemInformationType && length >= UuidOffset + UuidLength)
            {
                ReadOnlySpan<byte> strings = table[stringsStart..end];

                return new SmbiosSystemInformation(
                    ReadUuid(formatted.Slice(UuidOffset, UuidLength), major, minor),
                    ReadString(strings, formatted[0x04]),
                    ReadString(strings, formatted[0x05]),
                    ReadString(strings, formatted[0x07]));
            }

            if (type == EndOfTableType)
            {
                return null;
            }

            offset = end;
        }

        return null;
    }

    // SMBIOS 2.6 fixed the encoding of the first three UUID fields as little endian, which is also how
    // System.Guid stores them. Earlier tables used network order.
    private static Guid ReadUuid(ReadOnlySpan<byte> bytes, byte major, byte minor) =>
        major > 2 || (major == 2 && minor >= 6) ? new Guid(bytes) : new Guid(bytes, bigEndian: true);

    // The string set ends with two NULs. A structure with no strings is followed by two NULs as well.
    private static int FindStructureEnd(ReadOnlySpan<byte> table, int stringsStart)
    {
        for (int index = stringsStart; index + 1 < table.Length; index++)
        {
            if (table[index] == 0 && table[index + 1] == 0)
            {
                return index + 2;
            }
        }

        return -1;
    }

    private static string? ReadString(ReadOnlySpan<byte> strings, byte number)
    {
        if (number == 0)
        {
            return null;
        }

        int current = 1;
        int start = 0;

        for (int index = 0; index < strings.Length; index++)
        {
            if (strings[index] != 0)
            {
                continue;
            }

            if (current == number)
            {
                string value = Encoding.Latin1.GetString(strings[start..index]).Trim();

                return value.Length == 0 ? null : value;
            }

            current++;
            start = index + 1;
        }

        return null;
    }
}
