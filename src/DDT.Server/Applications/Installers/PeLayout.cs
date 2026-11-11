// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;

namespace DDT.Server.Applications.Installers;

// The sections, resources and overlay of a PE file, read from its headers alone. PeImage needs the whole file in
// memory, too much for an installer of a few hundred megabytes.
internal sealed class PeLayout
{
    // The PE format allows 96 sections; the headers of real programs fit in a few kilobytes.
    private const int MaxSections = 96;
    private const int MaxHeaderLength = 64 * 1024;
    private const int SectionHeaderLength = 40;
    private const int ResourceTableIndex = 2;
    private const int CertificateTableIndex = 4;

    private PeLayout(IReadOnlyList<PeSection> sections, uint resourceRva, long overlayOffset, long overlayLength)
    {
        Sections = sections;
        ResourceRva = resourceRva;
        OverlayOffset = overlayOffset;
        OverlayLength = overlayLength;
    }

    public IReadOnlyList<PeSection> Sections { get; }

    // Zero for a file without resources.
    public uint ResourceRva { get; }

    // The data appended after the last section, up to the signatures. Installers keep their payload there.
    public long OverlayOffset { get; }

    public long OverlayLength { get; }

    // Returns null when the file isn't a PE file.
    public static PeLayout? Read(Stream file)
    {
        byte[] headers = file.ReadAtMost(0, MaxHeaderLength);
        ReadOnlySpan<byte> span = headers;

        if (span.Length < 64 || span[0] != (byte)'M' || span[1] != (byte)'Z')
        {
            return null;
        }

        int pe = BinaryPrimitives.ReadInt32LittleEndian(span[0x3C..]);

        if (pe < 64 || pe > span.Length - 24 || BinaryPrimitives.ReadUInt32LittleEndian(span[pe..]) != 0x00004550)
        {
            return null;
        }

        int sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(span[(pe + 6)..]);
        int optionalLength = BinaryPrimitives.ReadUInt16LittleEndian(span[(pe + 20)..]);
        int optional = pe + 24;
        int sectionTable = optional + optionalLength;

        if (sectionCount > MaxSections || sectionTable + (sectionCount * SectionHeaderLength) > span.Length)
        {
            return null;
        }

        if (Directories(span.Slice(optional, optionalLength)) is not { } directories)
        {
            return null;
        }

        List<PeSection> sections = ReadSections(span.Slice(sectionTable, sectionCount * SectionHeaderLength), file.Length);
        long overlay = sections.Count == 0 ? 0 : sections.Max(section => section.RawOffset + section.RawLength);
        long end = directories.CertificateOffset >= overlay && directories.CertificateOffset < file.Length ? directories.CertificateOffset : file.Length;

        return new PeLayout(sections, directories.ResourceRva, overlay, Math.Max(0, end - overlay));
    }

    public PeSection? Section(string name) => Sections.FirstOrDefault(section => section.Name == name);

    public PeSection? SectionAt(uint rva) => Sections.FirstOrDefault(section => section.Contains(rva));

    private static (uint ResourceRva, long CertificateOffset)? Directories(ReadOnlySpan<byte> optional)
    {
        if (optional.Length < 2)
        {
            return null;
        }

        (int countOffset, int directories) = BinaryPrimitives.ReadUInt16LittleEndian(optional) switch
        {
            0x10B => (92, 96),
            0x20B => (108, 112),
            _ => (-1, -1),
        };

        if (countOffset < 0 || directories > optional.Length)
        {
            return null;
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(optional[countOffset..]);

        return (Entry(optional, directories, count, ResourceTableIndex), Entry(optional, directories, count, CertificateTableIndex));
    }

    // The certificate table's entry holds a file offset, the others an RVA.
    private static uint Entry(ReadOnlySpan<byte> optional, int directories, uint count, int index)
    {
        int at = directories + (index * 8);

        return index < count && at + 8 <= optional.Length ? BinaryPrimitives.ReadUInt32LittleEndian(optional[at..]) : 0;
    }

    // Sections whose bytes lie past the end of the file are cut to it.
    private static List<PeSection> ReadSections(ReadOnlySpan<byte> table, long fileLength)
    {
        List<PeSection> sections = [];

        for (int offset = 0; offset < table.Length; offset += SectionHeaderLength)
        {
            ReadOnlySpan<byte> header = table.Slice(offset, SectionHeaderLength);
            string name = Encoding.ASCII.GetString(header[..8]).TrimEnd('\0');
            long rawOffset = Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(header[20..]), fileLength);
            long rawLength = Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(header[16..]), fileLength - rawOffset);

            sections.Add(new PeSection(
                name,
                VirtualAddress: BinaryPrimitives.ReadUInt32LittleEndian(header[12..]),
                VirtualSize: BinaryPrimitives.ReadUInt32LittleEndian(header[8..]),
                rawOffset,
                rawLength));
        }

        return sections;
    }
}
