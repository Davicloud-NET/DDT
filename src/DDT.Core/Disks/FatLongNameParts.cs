// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;

namespace DDT.Core.Disks;

// Collects the long name entries in front of a short entry. A long name counts only when every part is there and their
// checksum names the short entry that follows.
internal sealed class FatLongNameParts
{
    private char[]? _name;
    private int _expected;
    private byte _checksum;

    public void Clear() => _name = null;

    // The last part comes first, marked with 0x40, and the parts count down to 1.
    public void Add(ReadOnlySpan<byte> entry)
    {
        int order = entry[0] & 0x1F;

        if ((entry[0] & 0x40) != 0 && order > 0)
        {
            _name = new char[order * FatNames.LongNameCharacters];
            _expected = order;
            _checksum = entry[13];
        }

        if (_name is null || order == 0 || order != _expected || entry[13] != _checksum)
        {
            _name = null;

            return;
        }

        Span<char> part = _name.AsSpan((order - 1) * FatNames.LongNameCharacters, FatNames.LongNameCharacters);

        for (int index = 0; index < FatNames.LongNameCharacters; index++)
        {
            part[index] = (char)BinaryPrimitives.ReadUInt16LittleEndian(entry[FatNames.LongNameCharacterOffset(index)..]);
        }

        _expected--;
    }

    // The parts are used up either way.
    public string? TakeFor(ReadOnlySpan<byte> shortEntry)
    {
        string? name = _name is not null && _expected == 0 && FatNames.Checksum(shortEntry) == _checksum ? LongName(_name) : null;
        _name = null;

        return name;
    }

    private static string LongName(char[] name)
    {
        int end = Array.IndexOf(name, '\0');

        return new string(name, 0, end < 0 ? name.Length : end);
    }
}
