// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DDT.Server.Tests;

// The parts of a WIM file the server reads, laid out as WimMetadata expects: the 208-byte header, stand-in image data,
// and the image list as UTF-16 LE XML with a byte order mark. The data is random, so no test sees another test's upload
// in the library.
internal static class TestWim
{
    public const int X86 = 0;
    public const int X64 = 9;
    public const int Arm64 = 12;

    private const int HeaderLength = 208;
    private const int DataLength = 4096;

    // One image per architecture code, indexed from 1.
    // null leaves out the ARCH element, like a captured data folder does.
    public static byte[] Create(params int?[] architectures) => Build(totalParts: 1, architectures);

    // The first part of a WIM split into two .swm files.
    public static byte[] CreateSplit(params int?[] architectures) => Build(totalParts: 2, architectures);

    public static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    private static byte[] Build(ushort totalParts, int?[] architectures)
    {
        ArgumentNullException.ThrowIfNull(architectures);

        StringBuilder xml = new("<WIM><TOTALBYTES>123456</TOTALBYTES>");

        for (int index = 1; index <= architectures.Length; index++)
        {
            xml.Append(CultureInfo.InvariantCulture, $"""
                <IMAGE INDEX="{index}">
                  <TOTALBYTES>{index * 10_000}</TOTALBYTES>
                  <HARDLINKBYTES>1000</HARDLINKBYTES>
                  <WINDOWS>
                    {(architectures[index - 1] is { } architecture ? $"<ARCH>{architecture}</ARCH>" : "")}
                    <EDITIONID>Edition{index}</EDITIONID>
                    <LANGUAGES><LANGUAGE>de-DE</LANGUAGE><DEFAULT>de-DE</DEFAULT></LANGUAGES>
                    <VERSION><MAJOR>10</MAJOR><MINOR>0</MINOR><BUILD>26100</BUILD><SPBUILD>1742</SPBUILD></VERSION>
                  </WINDOWS>
                  <NAME>Windows 11 Edition{index}</NAME>
                </IMAGE>
                """);
        }

        xml.Append("</WIM>");

        byte[] list = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(xml.ToString())];
        byte[] file = new byte[HeaderLength + DataLength + list.Length];
        Span<byte> header = file.AsSpan(0, HeaderLength);

        "MSWIM\0\0\0"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], HeaderLength);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], 0x10D00);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], 0x2 | 0x40000);
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..], 32768);
        BinaryPrimitives.WriteUInt16LittleEndian(header[40..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header[42..], totalParts);
        BinaryPrimitives.WriteUInt32LittleEndian(header[44..], (uint)architectures.Length);

        // The XML resource header: size in the file with no flags (stored uncompressed), offset, original size.
        BinaryPrimitives.WriteUInt64LittleEndian(header[72..], (ulong)list.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(header[80..], HeaderLength + DataLength);
        BinaryPrimitives.WriteUInt64LittleEndian(header[88..], (ulong)list.Length);

        RandomNumberGenerator.Fill(file.AsSpan(HeaderLength, DataLength));
        list.CopyTo(file, HeaderLength + DataLength);

        return file;
    }
}
