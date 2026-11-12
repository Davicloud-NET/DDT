// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;

namespace DDT.Server.Tests.Applications;

// NSIS installers: the firstheader as the overlay, then the header, stored, compressed on its own or as the start of
// one solid stream. The header holds the eight blocks, the entries and the string table.
internal static class TestNsis
{
    public const string Uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\";
    public const uint LocalMachine = 0x80000002;
    public const uint CurrentUser = 0x80000001;
    public const uint ShellContext = 0;

    private const int WriteRegistry = 51;
    private const int FirstHeaderLength = 28;

    public static byte[] Exe(byte[] header, string compression = "lzma", bool solid = true) =>
        TestExe.Create(new TestExeOptions { Overlay = Overlay(header, compression, solid) });

    // Entries writing a value under each key, a null key a language string, plus a file entry and a write elsewhere
    // that do not count. String offsets count characters.
    public static byte[] Header(IEnumerable<(uint Root, string? Key)> writes, bool unicode = true)
    {
        List<byte> strings = [.. Encode("\0", unicode)];
        int Add(string text)
        {
            int at = strings.Count / (unicode ? 2 : 1);
            strings.AddRange(Encode(text + "\0", unicode));

            return at;
        }

        List<int[]> entries = [[1, Add("$INSTDIR"), 0, 0, 0, 0, 0]];

        foreach ((uint root, string? key) in writes)
        {
            entries.Add([WriteRegistry, unchecked((int)root), key is null ? -5 : Add(key), Add("DisplayName"), Add("Example App"), 1, 1]);
        }

        entries.Add([WriteRegistry, unchecked((int)LocalMachine), Add(@"Software\Example"), Add("Path"), Add(@"C:\Example"), 1, 1]);

        const int start = 4 + (8 * 8);
        int entriesLength = entries.Count * 28;
        byte[] table = [.. strings];
        byte[] header = new byte[start + entriesLength + table.Length + 16];
        Block(header, 2, start, entries.Count);
        Block(header, 3, start + entriesLength, 0);
        Block(header, 4, start + entriesLength + table.Length, 0);

        for (int index = 0; index < entries.Count; index++)
        {
            for (int field = 0; field < 7; field++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(start + (index * 28) + (field * 4)), entries[index][field]);
            }
        }

        table.CopyTo(header, start + entriesLength);

        return header;
    }

    // The firstheader: flags, the signature, the header's length, the length of all from the firstheader on.
    public static byte[] Overlay(byte[] header, string compression, bool solid)
    {
        byte[] data = compression switch
        {
            "none" => [.. BitConverter.GetBytes(header.Length), .. header],
            _ when solid => Pack([.. BitConverter.GetBytes(header.Length), .. header, .. new byte[300]], compression),
            _ => Stored(Pack(header, compression)),
        };
        byte[] first = new byte[FirstHeaderLength];
        BinaryPrimitives.WriteUInt32LittleEndian(first.AsSpan(4), 0xDEADBEEF);
        "NullsoftInst"u8.CopyTo(first.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(first.AsSpan(20), header.Length);
        BinaryPrimitives.WriteInt32LittleEndian(first.AsSpan(24), FirstHeaderLength + data.Length + 4);

        return [.. first, .. data, .. new byte[4]];
    }

    private static byte[] Stored(byte[] packed) => [.. BitConverter.GetBytes((uint)packed.Length | 0x80000000), .. packed];

    private static byte[] Pack(byte[] data, string compression) =>
        compression == "lzma" ? TestCompression.Lzma(data) : TestCompression.Deflate(data);

    private static byte[] Encode(string text, bool unicode) => unicode ? Encoding.Unicode.GetBytes(text) : Encoding.Latin1.GetBytes(text);

    private static void Block(byte[] header, int index, int offset, int count)
    {
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4 + (index * 8)), offset);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8 + (index * 8)), count);
    }
}
