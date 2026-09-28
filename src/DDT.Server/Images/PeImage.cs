// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Security.Cryptography;

namespace DDT.Server.Images;

// The headers of a PE file, such as an EFI program, as far as Authenticode needs them (Microsoft's PE format
// specification). The file comes from an uploaded image, so every offset is checked against its length.
public sealed class PeImage
{
    public const ushort MachineAmd64 = 0x8664;
    public const ushort MachineArm64 = 0xAA64;
    public const ushort MachineI386 = 0x014C;

    private const int CertificateTableIndex = 4;
    private const int SectionHeaderBytes = 40;

    private readonly byte[] _file;
    private readonly Headers _headers;
    private readonly List<(int Offset, int Length)> _sections;

    private PeImage(byte[] file, Headers headers, List<(int Offset, int Length)> sections)
    {
        _file = file;
        _headers = headers;
        _sections = sections;
    }

    public ushort Machine => _headers.Machine;

    // Zero for a file without signatures.
    public int CertificateTableOffset => _headers.CertificateTableOffset;

    public int CertificateTableLength => _headers.CertificateTableLength;

    // Null when the file is no PE file DDT can read.
    public static PeImage? Read(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (ReadHeaders(file) is not { } headers)
        {
            return null;
        }

        return ReadSections(file, headers) is { } sections ? new PeImage(file, headers, sections) : null;
    }

    private static Headers? ReadHeaders(ReadOnlySpan<byte> span)
    {
        if (span.Length < 64 || span[0] != (byte)'M' || span[1] != (byte)'Z')
        {
            return null;
        }

        int pe = BinaryPrimitives.ReadInt32LittleEndian(span[0x3C..]);

        if (pe < 64 || pe > span.Length - 24 || BinaryPrimitives.ReadUInt32LittleEndian(span[pe..]) != 0x00004550)
        {
            return null;
        }

        ushort machine = BinaryPrimitives.ReadUInt16LittleEndian(span[(pe + 4)..]);
        int sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(span[(pe + 6)..]);
        int optionalHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(span[(pe + 20)..]);
        int optional = pe + 24;

        if (optional + optionalHeaderSize > span.Length || optionalHeaderSize < 96)
        {
            return null;
        }

        ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(span[optional..]);
        (int countOffset, int directories) = magic switch
        {
            0x10B => (92, 96),
            0x20B => (108, 112),
            _ => (-1, -1),
        };

        if (countOffset < 0 || directories > optionalHeaderSize)
        {
            return null;
        }

        int sizeOfHeaders = BinaryPrimitives.ReadInt32LittleEndian(span[(optional + 60)..]);
        uint directoryCount = BinaryPrimitives.ReadUInt32LittleEndian(span[(optional + countOffset)..]);
        int certificateEntry = optional + directories + (CertificateTableIndex * 8);
        bool signable = directoryCount > CertificateTableIndex && certificateEntry + 8 <= optional + optionalHeaderSize;

        if (CertificateTable(span, certificateEntry, signable) is not { } table)
        {
            return null;
        }

        int sectionTable = optional + optionalHeaderSize;

        if (sizeOfHeaders < certificateEntry + 8 || sizeOfHeaders > span.Length || sectionTable + ((long)sectionCount * SectionHeaderBytes) > span.Length)
        {
            return null;
        }

        return new Headers(machine, optional + 64, certificateEntry, sizeOfHeaders, sectionTable, sectionCount, table.Offset, table.Length);
    }

    // Zero for a file without the table's entry, and null for a table that does not lie within the file.
    private static (int Offset, int Length)? CertificateTable(ReadOnlySpan<byte> span, int entry, bool present)
    {
        if (!present)
        {
            return (0, 0);
        }

        (int Offset, int Length) table = (BinaryPrimitives.ReadInt32LittleEndian(span[entry..]), BinaryPrimitives.ReadInt32LittleEndian(span[(entry + 4)..]));

        return table.Length != 0 && (table.Offset <= 0 || table.Length < 0 || table.Offset > span.Length - table.Length) ? null : table;
    }

    private static List<(int Offset, int Length)>? ReadSections(ReadOnlySpan<byte> span, Headers headers)
    {
        List<(int Offset, int Length)> sections = [];

        for (int index = 0; index < headers.SectionCount; index++)
        {
            ReadOnlySpan<byte> section = span.Slice(headers.SectionTable + (index * SectionHeaderBytes), SectionHeaderBytes);
            int length = BinaryPrimitives.ReadInt32LittleEndian(section[16..]);
            int offset = BinaryPrimitives.ReadInt32LittleEndian(section[20..]);

            if (length == 0)
            {
                continue;
            }

            if (length < 0 || offset < 0 || offset > span.Length - length)
            {
                return null;
            }

            sections.Add((offset, length));
        }

        // The sections of a real program lie side by side. Ones that overlap would make the hash read the file many
        // times over, which a hostile image could use to keep the server busy for hours.
        if (sections.Sum(section => (long)section.Length) > span.Length)
        {
            return null;
        }

        sections.Sort((left, right) => left.Offset.CompareTo(right.Offset));

        return sections;
    }

    // The Authenticode hash as UEFI firmware computes it (EDK2's DxeImageVerificationLib): the headers without the
    // checksum and the certificate table entry, the sections in file order, then what follows them up to the
    // certificate table, which has to be the file's end.
    public byte[] Hash(HashAlgorithmName algorithm)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(algorithm);
        ReadOnlySpan<byte> file = _file;

        hash.AppendData(file[.._headers.ChecksumOffset]);
        hash.AppendData(file[(_headers.ChecksumOffset + 4).._headers.CertificateEntryOffset]);
        hash.AppendData(file[(_headers.CertificateEntryOffset + 8).._headers.SizeOfHeaders]);

        long hashed = _headers.SizeOfHeaders;

        foreach ((int offset, int length) in _sections)
        {
            hash.AppendData(file.Slice(offset, length));
            hashed += length;
        }

        long end = file.Length - CertificateTableLength;

        if (end > hashed)
        {
            hash.AppendData(file[(int)hashed..(int)end]);
        }

        return hash.GetHashAndReset();
    }

    // The PKCS #7 blobs of the WIN_CERTIFICATE entries of type PKCS_SIGNED_DATA, each 8-byte aligned. Null when the
    // table is damaged.
    public IReadOnlyList<byte[]>? Signatures()
    {
        List<byte[]> signatures = [];
        ReadOnlySpan<byte> table = _file.AsSpan(CertificateTableOffset, CertificateTableLength);
        int offset = 0;

        while (offset + 8 <= table.Length)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(table[offset..]);
            ushort revision = BinaryPrimitives.ReadUInt16LittleEndian(table[(offset + 4)..]);
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(table[(offset + 6)..]);

            if (length < 8 || length > table.Length - offset)
            {
                return null;
            }

            if (revision == 0x0200 && type == 0x0002)
            {
                signatures.Add(table.Slice(offset + 8, length - 8).ToArray());
            }

            offset += (length + 7) & ~7;
        }

        return signatures;
    }

    // Where the parts Authenticode needs lie in the file, every one within its length.
    private sealed record Headers(
        ushort Machine,
        int ChecksumOffset,
        int CertificateEntryOffset,
        int SizeOfHeaders,
        int SectionTable,
        int SectionCount,
        int CertificateTableOffset,
        int CertificateTableLength);
}
