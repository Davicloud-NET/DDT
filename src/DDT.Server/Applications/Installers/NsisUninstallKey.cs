// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;

namespace DDT.Server.Applications.Installers;

// The key under Uninstall an NSIS installer's WriteReg* instructions write, when they all name the same one as plain
// text. A variable, a language string or a shell folder in it, or two different keys, give null. The header holds
// blocks (pages, sections, entries, strings, ...), each an offset and a count; an entry is an opcode and six ints.
internal static class NsisUninstallKey
{
    // EW_WRITEREG in fileform.h, with every NSIS_SUPPORT_* of the default build, the same from 3.01 to 3.13.
    private const int WriteRegistry = 51;
    private const int EntryLength = 7 * 4;
    private const int EntriesBlock = 2;
    private const int StringsBlock = 3;
    private const int LanguageTablesBlock = 4;
    private const int FirstBlock = 4;
    private const int BlockLength = 8;
    private const int MaxStringLength = 8192;
    private const string UninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\";

    // HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE, and SHCTX, which is 0; REGROOTVIEW32 and REGROOTVIEW64 may be set.
    private const uint ViewBits = 0x60000000;
    private static readonly uint[] s_roots = [0, 0x80000001, 0x80000002];

    // REG_SZ, REG_BINARY and REG_DWORD, which exec.c takes as the value's type.
    private static readonly int[] s_valueTypes = [1, 3, 4];

    public static string? Read(Stream file, long overlayOffset)
    {
        ArgumentNullException.ThrowIfNull(file);

        return NsisHeaderData.FindFirstHeader(file, overlayOffset) is { } first && NsisHeaderData.Read(file, first) is { } header
            ? FromHeader(header)
            : null;
    }

    public static string? FromHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < FirstBlock + ((LanguageTablesBlock + 1) * BlockLength))
        {
            return null;
        }

        int entries = Block(header, EntriesBlock);
        int count = BinaryPrimitives.ReadInt32LittleEndian(header[(FirstBlock + (EntriesBlock * BlockLength) + 4)..]);
        int strings = Block(header, StringsBlock);
        int languageTables = Block(header, LanguageTablesBlock);

        if (entries <= 0 || count < 0 || count > (strings - entries) / EntryLength || strings > languageTables || languageTables > header.Length)
        {
            return null;
        }

        ReadOnlySpan<byte> table = header[strings..languageTables];
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        string? first = null;

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> entry = header.Slice(entries + (index * EntryLength), EntryLength);

            if (!IsWriteRegistry(entry))
            {
                continue;
            }

            (Found found, string? key) = UninstallKeyAt(table, BinaryPrimitives.ReadInt32LittleEndian(entry[8..]));

            if (found == Found.Unreadable)
            {
                return null;
            }

            if (found == Found.Key && key is not null && keys.Add(key))
            {
                first ??= key;
            }
        }

        return keys.Count == 1 ? first : null;
    }

    private static int Block(ReadOnlySpan<byte> header, int index) =>
        BinaryPrimitives.ReadInt32LittleEndian(header[(FirstBlock + (index * BlockLength))..]);

    private static bool IsWriteRegistry(ReadOnlySpan<byte> entry) =>
        BinaryPrimitives.ReadInt32LittleEndian(entry) == WriteRegistry
        && Array.IndexOf(s_roots, BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]) & ~ViewBits) >= 0
        && Array.IndexOf(s_valueTypes, BinaryPrimitives.ReadInt32LittleEndian(entry[20..])) >= 0;

    private enum Found
    {
        Other,
        Key,
        Unreadable,
    }

    // A key under Uninstall in either encoding, a key elsewhere, or one that cannot be told. A negative offset is a
    // language string, which may differ by language.
    private static (Found Found, string? Key) UninstallKeyAt(ReadOnlySpan<byte> table, int at)
    {
        if (at < 0)
        {
            return (Found.Unreadable, null);
        }

        string? ansi = Decode(table, at, unicode: false);
        string? unicode = Decode(table, at, unicode: true);
        bool ansiUninstall = IsUninstall(ansi);

        if (ansiUninstall == IsUninstall(unicode))
        {
            return (ansiUninstall ? Found.Unreadable : Found.Other, null);
        }

        string key = (ansiUninstall ? ansi : unicode) is { } text ? text[UninstallPath.Length..] : string.Empty;

        return IsPlain(key) ? (Found.Key, key) : (Found.Unreadable, null);
    }

    // A starts-with check that a code in the prefix part cannot fool: Decode leaves codes in as control characters.
    private static bool IsUninstall(string? text) =>
        text is not null && text.StartsWith(UninstallPath, StringComparison.OrdinalIgnoreCase);

    // Plain text, one key deep: no code, no control character, no backslash, no blank at either end.
    private static bool IsPlain(string key) =>
        key.Length > 0 && key.Trim().Length == key.Length && !key.Any(character => char.IsControl(character) || character == '\\');

    // NSIS 3 marks a language string, a shell folder and a variable with 1, 2 and 3, and escapes a literal with 4.
    // The escaped character is kept; the others stay as control characters, which IsPlain refuses.
    private static string? Decode(ReadOnlySpan<byte> table, int at, bool unicode)
    {
        int width = unicode ? 2 : 1;
        long start = (long)at * width;

        if (start >= table.Length)
        {
            return null;
        }

        StringBuilder text = new();

        for (long offset = start; offset + width <= table.Length && text.Length <= MaxStringLength; offset += width)
        {
            char character = unicode ? (char)BinaryPrimitives.ReadUInt16LittleEndian(table[(int)offset..]) : (char)table[(int)offset];

            if (character == 0)
            {
                return text.ToString();
            }

            if (!unicode && character >= 0x80)
            {
                return null;
            }

            if (character == 4 && offset + (2 * width) <= table.Length)
            {
                offset += width;
                character = unicode ? (char)BinaryPrimitives.ReadUInt16LittleEndian(table[(int)offset..]) : (char)table[(int)offset];
            }

            text.Append(character);
        }

        return null;
    }
}
