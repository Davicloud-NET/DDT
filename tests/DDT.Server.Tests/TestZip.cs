// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace DDT.Server.Tests;

// Zips as packages arrive, and the lies a crafted one can tell: the central directory, which readers trust, is
// patched after the zip is written.
internal static class TestZip
{
    private const uint CentralHeader = 0x02014b50;
    private const uint EndOfCentralDirectory = 0x06054b50;

    // A name ending in / is a folder. Files get random bytes, so every zip has its own hash.
    public static byte[] Create(params string[] names) =>
        Create(names.Select(name => (name, name.EndsWith('/') ? null : RandomNumberGenerator.GetBytes(64))).ToArray());

    public static byte[] Create(params (string Name, byte[]? Content)[] entries) =>
        Create(archive =>
        {
            foreach ((string name, byte[]? content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);

                if (content is not null)
                {
                    using Stream data = entry.Open();
                    data.Write(content);
                }
            }
        });

    public static byte[] Create(Action<ZipArchive> fill)
    {
        ArgumentNullException.ThrowIfNull(fill);

        using MemoryStream zip = new();

        using (ZipArchive archive = new(zip, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            fill(archive);
        }

        return zip.ToArray();
    }

    // Offsets within a central directory header.
    public const int Flags = 8;
    public const int Method = 10;
    public const int UncompressedSize = 24;

    public static void PatchCentral(byte[] zip, int entry, int field, uint value, int bytes = 4)
    {
        ArgumentNullException.ThrowIfNull(zip);

        int header = CentralHeaderOffset(zip, entry);
        Span<byte> target = zip.AsSpan(header + field, bytes);

        if (bytes == 2)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(target, (ushort)value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(target, value);
        }
    }

    private static int CentralHeaderOffset(byte[] zip, int entry)
    {
        int end = zip.Length - 22;

        while (BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(end)) != EndOfCentralDirectory)
        {
            end--;
        }

        int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(end + 16));

        for (int index = 0; ; index++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(offset)) != CentralHeader)
            {
                throw new InvalidOperationException("The zip has no such entry.");
            }

            if (index == entry)
            {
                return offset;
            }

            offset += 46
                + BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(offset + 28))
                + BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(offset + 30))
                + BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(offset + 32));
        }
    }
}
