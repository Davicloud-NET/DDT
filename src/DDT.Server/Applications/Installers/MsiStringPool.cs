// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;

namespace DDT.Server.Applications.Installers;

// An MSI's strings. _StringPool holds a length and a reference count per string, _StringData the bytes back to back
// in the database's code page. The first pool entry holds the code page, its top bit says references take 3 bytes.
// The layout follows Wine's msi/string.c.
internal sealed class MsiStringPool
{
    private const int LongReferencesFlag = 0x8000;
    private const int NeutralCodePage = 0;
    private const int WindowsLatin1 = 1252;

    private readonly byte[] _pool;
    private readonly byte[] _data;
    private readonly Encoding _encoding;

    public MsiStringPool(byte[] pool, byte[] data)
    {
        if (pool.Length % 4 != 0)
        {
            throw new InvalidDataException("The string pool is damaged.");
        }

        _pool = pool;
        _data = data;

        ushort low = pool.Length >= 4 ? Word(0) : (ushort)0;
        ushort high = pool.Length >= 4 ? Word(1) : (ushort)0;
        ReferenceSize = (high & LongReferencesFlag) != 0 ? 3 : 2;
        _encoding = EncodingOf(low | ((high & ~LongReferencesFlag) << 16));
    }

    public int ReferenceSize { get; }

    // Decodes the wanted string ids, walking the pool once. Id 0 is null.
    public Dictionary<int, string> Strings(IReadOnlySet<int> wanted)
    {
        Dictionary<int, string> strings = [];
        int slots = _pool.Length / 4;
        int offset = 0;
        int id = 1;

        for (int slot = 1; slot < slots; id++)
        {
            (int length, int used) = Entry(slot, slots);

            if (length > _data.Length - offset)
            {
                throw new InvalidDataException("The string pool reaches past the string data.");
            }

            if (wanted.Contains(id))
            {
                strings[id] = _encoding.GetString(_data, offset, length);
            }

            offset += length;
            slot += used;
        }

        return strings;
    }

    // A string over 64 KB takes two slots: a zero length with the reference count, then the length's low and high words.
    private (int Length, int Slots) Entry(int slot, int slots)
    {
        ushort length = Word(slot * 2);
        ushort references = Word((slot * 2) + 1);

        if (length != 0 || references == 0)
        {
            return (length, 1);
        }

        if (slot + 1 >= slots)
        {
            throw new InvalidDataException("A long string's length is missing.");
        }

        int longLength = Word((slot + 1) * 2) | (Word(((slot + 1) * 2) + 1) << 16);

        return longLength < 0 ? throw new InvalidDataException("A string is too long.") : (longLength, 2);
    }

    private ushort Word(int index) => BinaryPrimitives.ReadUInt16LittleEndian(_pool.AsSpan(index * 2));

    private static Encoding EncodingOf(int codePage)
    {
        // A neutral database should hold only ASCII. Windows reads it in the system's code page, mostly 1252.
        int effective = codePage == NeutralCodePage ? WindowsLatin1 : codePage;

        if (CodePagesEncodingProvider.Instance.GetEncoding(effective) is { } provided)
        {
            return provided;
        }

        try
        {
            return Encoding.GetEncoding(effective);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException("The database's code page is unknown.", exception);
        }
    }
}
