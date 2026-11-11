// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace DDT.Server.Tests.Applications;

// MSIs for the reader's tests: the one Windows Installer wrote under Fixtures/Installers, and synthetic ones with only
// the string pool and the Property table, laid out as Wine's msi writes them.
internal static class TestMsi
{
    public const string ProductCode = "{6F1C2E5A-3B7D-4C9E-8A21-0D4F5B6C7E81}";
    public const string UpgradeCode = "{0A9B8C7D-6E5F-4A3B-9C2D-1E0F2A3B4C5D}";

    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz._";

    public static byte[] Fixture()
    {
        using FileStream file = File.OpenRead(Path.Combine(Repository.Root(), "tests", "DDT.Server.Tests", "Fixtures", "Installers", "properties.msi.gz"));
        using GZipStream gzip = new(file, CompressionMode.Decompress);
        using MemoryStream content = new();
        gzip.CopyTo(content);

        return content.ToArray();
    }

    public static (string Name, string Value)[] Product(string name = "Example App") =>
    [
        ("ALLUSERS", "1"),
        ("ProductCode", ProductCode),
        ("ProductName", name),
        ("ProductVersion", "4.5.6"),
        ("Manufacturer", "Example Ltd"),
        ("UpgradeCode", UpgradeCode),
    ];

    public static byte[] Create(TestMsiOptions options) =>
        TestCompoundFile.Create(Streams(options), options.SectorSize);

    public static (string Name, byte[] Data)[] Streams(TestMsiOptions options)
    {
        Encoding encoding = CodePagesEncodingProvider.Instance.GetEncoding(options.CodePage) ?? Encoding.GetEncoding(options.CodePage);
        List<string> strings = [.. options.Properties.SelectMany(property => new[] { property.Name, property.Value }).Distinct(StringComparer.Ordinal)];
        int Id(string value) => options.EmptyStringsFirst + strings.IndexOf(value) + 1;

        int size = options.LongReferences ? 3 : 2;
        int rows = options.Properties.Count;
        byte[] table = new byte[rows * 2 * size];

        for (int row = 0; row < rows; row++)
        {
            Reference(table, row * size, size, Id(options.Properties[row].Name));
            Reference(table, (rows + row) * size, size, Id(options.Properties[row].Value));
        }

        (byte[] pool, byte[] data) = Pool(strings, encoding, options);

        return
        [
            ("\u0005SummaryInformation", new byte[48]),
            (Table("_StringPool"), pool),
            (Table("_StringData"), data),
            (Table("Property"), table),
        ];
    }

    // A table's stream name: the table marker, then the name packed two characters to one.
    public static string Table(string name)
    {
        StringBuilder encoded = new("䡀");

        for (int index = 0; index < name.Length; index += 2)
        {
            int first = Alphabet.IndexOf(name[index], StringComparison.Ordinal);
            encoded.Append(index + 1 < name.Length
                ? (char)(0x3800 + first + (Alphabet.IndexOf(name[index + 1], StringComparison.Ordinal) << 6))
                : (char)(0x4800 + first));
        }

        return encoded.ToString();
    }

    private static (byte[] Pool, byte[] Data) Pool(List<string> strings, Encoding encoding, TestMsiOptions options)
    {
        List<ushort> words = [(ushort)(options.CodePage & 0xFFFF), (ushort)((options.CodePage >> 16) | (options.LongReferences ? 0x8000 : 0))];
        words.AddRange(Enumerable.Repeat((ushort)0, options.EmptyStringsFirst * 2));
        List<byte> data = [];

        foreach (string value in strings)
        {
            byte[] bytes = encoding.GetBytes(value);
            data.AddRange(bytes);

            // Over 64 KB: a zero length with the reference count, then the length's two halves.
            words.AddRange(bytes.Length > ushort.MaxValue
                ? [0, 1, (ushort)(bytes.Length & 0xFFFF), (ushort)(bytes.Length >> 16)]
                : [(ushort)bytes.Length, 1]);
        }

        byte[] pool = new byte[words.Count * 2];

        for (int index = 0; index < words.Count; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(pool.AsSpan(index * 2), words[index]);
        }

        return (pool, [.. data]);
    }

    private static void Reference(byte[] table, int offset, int size, int id)
    {
        table[offset] = (byte)id;
        table[offset + 1] = (byte)(id >> 8);

        if (size == 3)
        {
            table[offset + 2] = (byte)(id >> 16);
        }
    }
}
