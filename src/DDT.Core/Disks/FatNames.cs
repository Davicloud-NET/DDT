// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text;

namespace DDT.Core.Disks;

// The 8.3 names of FAT directory entries and the long names beside them, as Microsoft's FAT specification defines them.
internal static class FatNames
{
    public const int ShortNameLength = 11;

    // How many characters of a long name fit in one long name entry.
    public const int LongNameCharacters = 13;

    private const string ShortNameSymbols = "$%'-_@~`!(){}^#&";

    // The checksum of a short entry. Its long name entries carry it.
    public static byte Checksum(ReadOnlySpan<byte> shortName)
    {
        byte sum = 0;

        foreach (byte value in shortName[..ShortNameLength])
        {
            sum = (byte)(((sum & 1) << 7) + (sum >> 1) + value);
        }

        return sum;
    }

    public static int LongNameEntries(string name) => (name.Length + LongNameCharacters - 1) / LongNameCharacters;

    // The byte offset of a long name character in its entry. An entry holds five characters from byte 1, six from
    // byte 14 and two from byte 28.
    public static int LongNameCharacterOffset(int index) => index switch
    {
        < 5 => 1 + (index * 2),
        < 11 => 14 + ((index - 5) * 2),
        _ => 28 + ((index - 11) * 2),
    };

    // The name as the 11 bytes of a short entry. Returns null when it needs a long name, because it has lower case
    // letters, more than 8 and 3 characters, or characters an 8.3 name can't have.
    public static byte[]? AsShortName(string name)
    {
        if (name is "." or "..")
        {
            return null;
        }

        int dot = name.LastIndexOf('.');
        string basis = dot < 0 ? name : name[..dot];
        string extension = dot < 0 ? "" : name[(dot + 1)..];

        if (basis.Length is 0 or > 8 || extension.Length > 3 || !IsShortPart(basis) || !IsShortPart(extension))
        {
            return null;
        }

        return Encoding.ASCII.GetBytes(basis.PadRight(8) + extension.PadRight(3));
    }

    // Makes a short name for a name that needs a long one, unique among taken. It's the first six usable characters,
    // ~ and a number, plus the first three characters of the extension.
    public static byte[] Generate(string name, ISet<string> taken)
    {
        int dot = name.LastIndexOf('.');
        string basis = Usable(dot <= 0 ? name : name[..dot]);
        string extension = Usable(dot <= 0 ? "" : name[(dot + 1)..]);
        extension = extension.Length > 3 ? extension[..3] : extension;

        if (basis.Length == 0)
        {
            basis = "FILE";
        }

        for (int number = 1; number < 1_000_000; number++)
        {
            string tail = string.Create(CultureInfo.InvariantCulture, $"~{number}");
            string head = basis.Length > 8 - tail.Length ? basis[..(8 - tail.Length)] : basis;
            string candidate = (head + tail).PadRight(8) + extension.PadRight(3);

            if (taken.Add(candidate))
            {
                return Encoding.ASCII.GetBytes(candidate);
            }
        }

        throw new InvalidOperationException($"No short name is left for {name}.");
    }

    // Reads a short entry's name without the padding. A part is lower case when the entry's flags say so. Windows NT
    // sets those flags for lower case names that fit 8.3, such as grubx64.efi.
    public static string Read(ReadOnlySpan<byte> entry)
    {
        char[] bytes = new char[ShortNameLength];

        for (int index = 0; index < ShortNameLength; index++)
        {
            bytes[index] = (char)entry[index];
        }

        // 0x05 stands for 0xE5, which marks a deleted entry.
        if (bytes[0] == '\u0005')
        {
            bytes[0] = 'å';
        }

        string basis = new string(bytes, 0, 8).TrimEnd(' ');
        string extension = new string(bytes, 8, 3).TrimEnd(' ');
        byte flags = entry[12];

        if ((flags & 0x08) != 0)
        {
            basis = basis.ToLowerInvariant();
        }

        if ((flags & 0x10) != 0)
        {
            extension = extension.ToLowerInvariant();
        }

        return extension.Length == 0 ? basis : $"{basis}.{extension}";
    }

    private static bool IsShortPart(string part) =>
        part.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || ShortNameSymbols.Contains(c, StringComparison.Ordinal));

    private static string Usable(string part) =>
        new([.. part.ToUpperInvariant().Where(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || ShortNameSymbols.Contains(c, StringComparison.Ordinal))]);
}
