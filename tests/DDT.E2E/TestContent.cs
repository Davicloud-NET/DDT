// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace DDT.E2E;

// What the tests upload. Random bytes make every file new to the library, and their size decides how long an agent
// takes to download one, which is how long a test has to act while a step runs.
internal static class TestContent
{
    private const int WimHeaderLength = 208;
    private const int Megabyte = 1024 * 1024;

    // As much of a WIM as the server and a dry run read: the header, data that stands in for the image, and the image
    // list, uncompressed UTF-16 LE with a byte order mark, naming one x64 image. A dry run applies nothing, so no
    // real image and no boot image build is needed.
    public static void WriteWim(string path, int dataMegabytes)
    {
        byte[] list =
        [
            0xFF, 0xFE,
            .. Encoding.Unicode.GetBytes(
                "<WIM><TOTALBYTES>1</TOTALBYTES><IMAGE INDEX=\"1\"><TOTALBYTES>10000000</TOTALBYTES><WINDOWS><ARCH>9</ARCH>" +
                "<EDITIONID>Professional</EDITIONID><LANGUAGES><LANGUAGE>en-US</LANGUAGE><DEFAULT>en-US</DEFAULT></LANGUAGES>" +
                "<VERSION><MAJOR>10</MAJOR><MINOR>0</MINOR><BUILD>26100</BUILD><SPBUILD>1</SPBUILD></VERSION></WINDOWS>" +
                "<NAME>DDT E2E Windows</NAME></IMAGE></WIM>"),
        ];
        long dataLength = (long)dataMegabytes * Megabyte;
        byte[] header = new byte[WimHeaderLength];

        "MSWIM\0\0\0"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), WimHeaderLength);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 0x10D00);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), 0x2 | 0x40000);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), 32768);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(42), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44), 1);

        // The image list's resource: its size in the file without flags, its offset and its size.
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(72), (ulong)list.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(80), (ulong)(WimHeaderLength + dataLength));
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(88), (ulong)list.Length);

        using FileStream file = File.Create(path);
        file.Write(header);
        WriteRandom(file, dataLength);
        file.Write(list);
    }

    // A driver package needs an .inf, which the server checks for.
    public static void WriteDriverPackage(string path)
    {
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(zip, "e2e/e2e.inf", Encoding.ASCII.GetBytes("[Version]\r\nSignature=\"$WINDOWS NT$\"\r\nClass=System\r\n"));
        WriteEntry(zip, "e2e/e2e.sys", RandomNumberGenerator.GetBytes(4096));
    }

    // A files package with a readme, and dataMegabytes of random data stored uncompressed.
    public static void WriteFilesPackage(string path, int dataMegabytes)
    {
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(zip, "readme.txt", Encoding.ASCII.GetBytes($"DDT E2E files {Guid.NewGuid():N}\r\n"));

        if (dataMegabytes > 0)
        {
            using Stream data = zip.CreateEntry("data.bin", CompressionLevel.NoCompression).Open();
            WriteRandom(data, (long)dataMegabytes * Megabyte);
        }
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] content)
    {
        using Stream entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        entry.Write(content);
    }

    private static void WriteRandom(Stream stream, long length)
    {
        byte[] buffer = new byte[Megabyte];

        for (long written = 0; written < length; written += buffer.Length)
        {
            RandomNumberGenerator.Fill(buffer);
            stream.Write(buffer, 0, (int)Math.Min(buffer.Length, length - written));
        }
    }
}
