// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;

namespace DDT.Server.Applications.Installers;

// The header of an OLE compound file, laid out as in [MS-CFB] section 2.2.
internal sealed record CompoundFileHeader(
    int SectorShift,
    bool HasLongStreamSizes,
    uint FirstDirectorySector,
    uint FirstMiniFatSector,
    uint MiniFatSectorCount,
    uint FirstDifatSector,
    uint DifatSectorCount,
    uint FatSectorCount,
    IReadOnlyList<uint> HeaderDifat)
{
    public const int Length = 512;
    public const int MiniSectorSize = 64;
    public const uint MiniStreamCutoff = 4096;

    private const int HeaderDifatOffset = 0x4C;
    private const int HeaderDifatCount = 109;

    private static readonly byte[] s_signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    public int SectorSize => 1 << SectorShift;

    public static bool HasSignature(ReadOnlySpan<byte> start) => start.StartsWith(s_signature);

    public static CompoundFileHeader Parse(ReadOnlySpan<byte> header)
    {
        if (header.Length < Length || !HasSignature(header) || BinaryPrimitives.ReadUInt16LittleEndian(header[0x1C..]) != 0xFFFE)
        {
            throw new InvalidDataException("Not a compound file.");
        }

        ushort major = BinaryPrimitives.ReadUInt16LittleEndian(header[0x1A..]);
        ushort sectorShift = BinaryPrimitives.ReadUInt16LittleEndian(header[0x1E..]);
        ushort miniSectorShift = BinaryPrimitives.ReadUInt16LittleEndian(header[0x20..]);
        uint cutoff = BinaryPrimitives.ReadUInt32LittleEndian(header[0x38..]);

        if (!(major == 3 && sectorShift == 9) && !(major == 4 && sectorShift == 12))
        {
            throw new InvalidDataException("Unknown compound file version.");
        }

        if (miniSectorShift != 6 || cutoff != MiniStreamCutoff)
        {
            throw new InvalidDataException("Unknown mini stream layout.");
        }

        uint[] difat = new uint[HeaderDifatCount];

        for (int index = 0; index < HeaderDifatCount; index++)
        {
            difat[index] = BinaryPrimitives.ReadUInt32LittleEndian(header[(HeaderDifatOffset + (index * 4))..]);
        }

        return new CompoundFileHeader(
            sectorShift,
            major == 4,
            FirstDirectorySector: BinaryPrimitives.ReadUInt32LittleEndian(header[0x30..]),
            FirstMiniFatSector: BinaryPrimitives.ReadUInt32LittleEndian(header[0x3C..]),
            MiniFatSectorCount: BinaryPrimitives.ReadUInt32LittleEndian(header[0x40..]),
            FirstDifatSector: BinaryPrimitives.ReadUInt32LittleEndian(header[0x44..]),
            DifatSectorCount: BinaryPrimitives.ReadUInt32LittleEndian(header[0x48..]),
            FatSectorCount: BinaryPrimitives.ReadUInt32LittleEndian(header[0x2C..]),
            difat);
    }
}
