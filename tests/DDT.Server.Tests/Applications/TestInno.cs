// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;
using DDT.Core;

namespace DDT.Server.Tests.Applications;

// Inno Setup installers: the loader's offset table as a resource, setup-0.bin as the overlay, its header one LZMA block
// in CRC'd chunks.
internal static class TestInno
{
    public const int EncryptionHeaderLength = 4 + 49;

    private const int ChunkLength = 4096;

    public static byte[] Exe(byte[] setupData, int tableVersion = 2)
    {
        int offset0 = TestExe.Create(new TestExeOptions { InnoOffsetTable = OffsetTable(0, tableVersion) }).Length;

        return TestExe.Create(new TestExeOptions { InnoOffsetTable = OffsetTable(offset0, tableVersion), Overlay = setupData });
    }

    // Version 1 has Offset0 at 32 and 44 bytes, version 2 an Int64 at 40 and 64; the CRC is the last field.
    public static byte[] OffsetTable(long offset0, int version)
    {
        byte[] table = new byte[version == 1 ? 44 : 64];
        byte[] id = [.. "rDlPtS"u8, 0xCD, 0xE6, 0xD7, 0x7B, 0x0B, 0x2A];
        id.CopyTo(table, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(12), (uint)version);

        if (version == 1)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(32), (uint)offset0);
        }
        else
        {
            BinaryPrimitives.WriteInt64LittleEndian(table.AsSpan(40), offset0);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(table.Length - 4), Crc32.Append(0, table.AsSpan(0, table.Length - 4)));

        return table;
    }

    // The header's strings up to Uninstallable: AppName, AppVerName, AppId, ..., CreateUninstallRegKey, Uninstallable.
    public static string[] Strings(string appId, string createKey = "yes", string uninstallable = "yes", int createKeyIndex = 24)
    {
        string[] strings = Enumerable.Repeat(string.Empty, createKeyIndex + 2).ToArray();
        strings[0] = "Example App";
        strings[1] = "Example App 1.0";
        strings[2] = appId;
        strings[createKeyIndex] = createKey;
        strings[createKeyIndex + 1] = uninstallable;

        return strings;
    }

    // Each string a byte length then UTF-16 or single bytes, then more of the header.
    public static byte[] Header(IEnumerable<string> strings, bool unicode = true)
    {
        List<byte> header = [];

        foreach (string text in strings)
        {
            byte[] bytes = unicode ? Encoding.Unicode.GetBytes(text) : Encoding.Latin1.GetBytes(text);
            header.AddRange(BitConverter.GetBytes(bytes.Length));
            header.AddRange(bytes);
        }

        return [.. header, .. new byte[64]];
    }

    // The SetupID, the encryption header from 6.5 on, the block header (CRC, stored size, compressed), the chunks.
    public static byte[] SetupData(string version, byte[] header, bool encryptionHeader = true, bool longSize = true, byte encryption = 0)
    {
        byte[] id = new byte[64];
        Encoding.ASCII.GetBytes($"Inno Setup Setup Data ({version})").CopyTo(id, 0);
        List<byte> data = [.. id];

        if (encryptionHeader)
        {
            byte[] fields = new byte[EncryptionHeaderLength - 4];
            fields[0] = encryption;
            data.AddRange(Crc(fields));
            data.AddRange(fields);
        }

        byte[] compressed = TestCompression.Lzma(header);
        List<byte> chunks = [];

        for (int start = 0; start < compressed.Length; start += ChunkLength)
        {
            byte[] chunk = compressed[start..Math.Min(compressed.Length, start + ChunkLength)];
            chunks.AddRange(Crc(chunk));
            chunks.AddRange(chunk);
        }

        byte[] block = new byte[(longSize ? 8 : 4) + 1];

        if (longSize)
        {
            BinaryPrimitives.WriteInt64LittleEndian(block, chunks.Count);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(block, chunks.Count);
        }

        block[^1] = 1;

        return [.. data, .. Crc(block), .. block, .. chunks, .. new byte[100]];
    }

    private static byte[] Crc(ReadOnlySpan<byte> data)
    {
        byte[] crc = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(crc, Crc32.Append(0, data));

        return crc;
    }
}
