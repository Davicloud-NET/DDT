// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace DDT.Agent;

// Parses GetSystemFirmwareTable's 'RSMB' buffer: an 8-byte RawSMBIOSData header, then the SMBIOS structure table.
// Reading it directly spares the boot image the WinPE-WMI optional component.
public static class SmbiosParser
{
    private const int RawHeaderLength = 8;
    private const byte BiosInformationType = 0;
    private const byte SystemInformationType = 1;
    private const byte BaseboardInformationType = 2;
    private const byte SystemEnclosureType = 3;
    private const byte ProcessorInformationType = 4;
    private const byte EndOfTableType = 127;
    private const int UuidOffset = 0x08;
    private const int UuidLength = 16;
    private const int ChassisTypeOffset = 0x05;

    // Where DMTF DSP0134 puts the string numbers and the processor's status. A structure from an older SMBIOS version
    // is shorter, and lacks the fields added after it.
    private const int BiosVersionOffset = 0x05;
    private const int BiosDateOffset = 0x08;
    private const int SystemVersionOffset = 0x06;
    private const int SystemSkuOffset = 0x19;
    private const int SystemFamilyOffset = 0x1A;
    private const int BaseboardProductOffset = 0x05;
    private const int AssetTagOffset = 0x08;
    private const int ProcessorVersionOffset = 0x10;
    private const int ProcessorStatusOffset = 0x18;

    // The top bit of the chassis type says whether the enclosure has a lock, which says nothing about its kind.
    private const byte ChassisTypeMask = 0x7F;

    // Bit 6 of a processor's status says that its socket holds one. Servers list their empty sockets too.
    private const byte SocketPopulated = 0x40;

    // Reads the first structure of each type, which firmware lists in any order. A malformed structure ends the pass,
    // as nothing after it can be found safely, but keeps what was read before it.
    public static SmbiosSystemInformation? TryReadSystemInformation(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < RawHeaderLength)
        {
            return null;
        }

        int tableLength = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(raw[4..]), (uint)(raw.Length - RawHeaderLength));
        ReadOnlySpan<byte> table = raw.Slice(RawHeaderLength, tableLength);
        Findings found = new(major: raw[1], minor: raw[2]);
        int offset = 0;

        while (TryReadStructure(table, ref offset, out Structure structure))
        {
            found.Read(structure);
        }

        return found.ToSystemInformation();
    }

    // The structure at offset, and offset moved past it; false at the end of the table or at a malformed structure.
    private static bool TryReadStructure(ReadOnlySpan<byte> table, ref int offset, out Structure structure)
    {
        structure = default;

        if (offset + 4 > table.Length)
        {
            return false;
        }

        byte type = table[offset];
        byte length = table[offset + 1];

        if (length < 4 || offset + length > table.Length)
        {
            return false;
        }

        int stringsStart = offset + length;
        int end = FindStructureEnd(table, stringsStart);

        if (end < 0 || type == EndOfTableType)
        {
            return false;
        }

        structure = new Structure(type, table.Slice(offset, length), table[stringsStart..end]);
        offset = end;

        return true;
    }

    // The BIOS release date is mm/dd/yyyy, or mm/dd/yy for 19yy in tables from before SMBIOS 2.3. Anything else is not a
    // date the agent can report.
    private static string? ReleaseDate(string? value)
    {
        string[] parts = value?.Split('/') ?? [];

        if (parts.Length != 3
            || parts[0].Length is not (1 or 2)
            || parts[1].Length is not (1 or 2)
            || parts[2].Length is not (2 or 4)
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int month)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int day)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int year))
        {
            return null;
        }

        year = parts[2].Length == 2 ? 1900 + year : year;

        return year >= 1 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month)
            ? string.Create(CultureInfo.InvariantCulture, $"{year:D4}-{month:D2}-{day:D2}")
            : null;
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

    // The string whose number the structure holds at offset, null when the structure is too short to have that field.
    private static string? StringAt(ReadOnlySpan<byte> formatted, ReadOnlySpan<byte> strings, int offset) =>
        offset < formatted.Length ? ReadString(strings, formatted[offset]) : null;

    // Strings are numbered from 1, and 0 names none.
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

    // One structure: its formatted area, which starts with the type and the length, and its strings.
    private readonly ref struct Structure(byte type, ReadOnlySpan<byte> formatted, ReadOnlySpan<byte> strings)
    {
        public byte Type { get; } = type;

        public ReadOnlySpan<byte> Formatted { get; } = formatted;

        public ReadOnlySpan<byte> Strings { get; } = strings;

        // The string whose number the structure holds at offset.
        public string? StringAt(int offset) => SmbiosParser.StringAt(Formatted, Strings, offset);
    }

    // What the pass found so far: the first structure of each type the agent reports on.
    private sealed class Findings(byte major, byte minor)
    {
        private SmbiosSystemInformation? _system;
        private byte? _chassisType;
        private string? _biosVersion;
        private string? _biosDate;
        private string? _baseboardProduct;
        private string? _assetTag;
        private string? _processorVersion;
        private bool _bios;
        private bool _baseboard;
        private bool _enclosure;
        private bool _processor;

        public void Read(Structure structure)
        {
            ReadOnlySpan<byte> formatted = structure.Formatted;

            switch (structure.Type)
            {
                case BiosInformationType when !_bios:
                    _bios = true;
                    _biosVersion = structure.StringAt(BiosVersionOffset);
                    _biosDate = ReleaseDate(structure.StringAt(BiosDateOffset));
                    break;

                case SystemInformationType when _system is null && formatted.Length >= UuidOffset + UuidLength:
                    _system = new SmbiosSystemInformation(
                        ReadUuid(formatted.Slice(UuidOffset, UuidLength), major, minor),
                        ReadString(structure.Strings, formatted[0x04]),
                        ReadString(structure.Strings, formatted[0x05]),
                        ReadString(structure.Strings, formatted[0x07]),
                        null)
                    {
                        Version = structure.StringAt(SystemVersionOffset),
                        Sku = structure.StringAt(SystemSkuOffset),
                        Family = structure.StringAt(SystemFamilyOffset),
                    };
                    break;

                case BaseboardInformationType when !_baseboard:
                    _baseboard = true;
                    _baseboardProduct = structure.StringAt(BaseboardProductOffset);
                    break;

                case SystemEnclosureType when !_enclosure && formatted.Length > ChassisTypeOffset:
                    _enclosure = true;
                    _chassisType = (byte)(formatted[ChassisTypeOffset] & ChassisTypeMask);
                    _assetTag = structure.StringAt(AssetTagOffset);
                    break;

                case ProcessorInformationType when !_processor && formatted.Length > ProcessorStatusOffset
                    && (formatted[ProcessorStatusOffset] & SocketPopulated) != 0:
                    _processor = true;
                    _processorVersion = structure.StringAt(ProcessorVersionOffset);
                    break;
            }
        }

        // Null without a System Information structure.
        public SmbiosSystemInformation? ToSystemInformation() => _system is null
            ? null
            : _system with
            {
                ChassisType = _chassisType,
                BiosVersion = _biosVersion,
                BiosDate = _biosDate,
                BaseboardProduct = _baseboardProduct,
                AssetTag = _assetTag,
                ProcessorVersion = _processorVersion,
            };
    }
}
