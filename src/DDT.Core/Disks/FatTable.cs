// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;

namespace DDT.Core.Disks;

// A file allocation table: the next cluster of every cluster, packed as FAT12, FAT16 or FAT32 packs it.
internal sealed class FatTable(FatType type, byte[] bytes)
{
    public byte[] Bytes => bytes;

    // What FatVolumeBuilder writes to end a chain; IsEnd also takes the reserved values just below it.
    public uint EndMarker => type switch
    {
        FatType.Fat12 => 0xFFF,
        FatType.Fat16 => 0xFFFF,
        _ => 0x0FFFFFFF,
    };

    // Includes the two reserved entries in front of cluster 2.
    public static long BytesFor(FatType type, long clusters) => type switch
    {
        FatType.Fat12 => (((clusters + 2) * 3) + 1) / 2,
        FatType.Fat16 => (clusters + 2) * 2,
        _ => (clusters + 2) * 4,
    };

    public bool IsEnd(uint value) => type switch
    {
        FatType.Fat12 => value >= 0xFF8,
        FatType.Fat16 => value >= 0xFFF8,
        _ => value >= 0x0FFFFFF8,
    };

    // FAT12 packs two entries into three bytes: an even cluster in the low 12 bits of its pair, an odd one in the high.
    public uint Get(uint cluster) => type switch
    {
        FatType.Fat12 => (cluster & 1) == 0
            ? (uint)(BinaryPrimitives.ReadUInt16LittleEndian(Fat12Pair(cluster)) & 0x0FFF)
            : (uint)(BinaryPrimitives.ReadUInt16LittleEndian(Fat12Pair(cluster)) >> 4),
        FatType.Fat16 => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan((int)(cluster * 2))),
        _ => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)(cluster * 4))) & 0x0FFFFFFF,
    };

    public void Set(uint cluster, uint value)
    {
        switch (type)
        {
            case FatType.Fat12:
                Span<byte> pair = Fat12Pair(cluster);
                uint word = BinaryPrimitives.ReadUInt16LittleEndian(pair);
                word = (cluster & 1) == 0 ? (word & 0xF000) | value : (word & 0x000F) | (value << 4);
                BinaryPrimitives.WriteUInt16LittleEndian(pair, (ushort)word);
                break;
            case FatType.Fat16:
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan((int)(cluster * 2)), (ushort)value);
                break;
            default:
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((int)(cluster * 4)), value);
                break;
        }
    }

    private Span<byte> Fat12Pair(uint cluster) => bytes.AsSpan((int)(cluster + (cluster / 2)), 2);
}
