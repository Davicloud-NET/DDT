// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;

namespace DDT.Core.Disks;

// The directory entries of FatVolumeBuilder's nodes, all stamped with one time. The root holds the volume label.
internal sealed class FatDirectoryWriter(string label, DateTime timestamp)
{
    private const int DirectoryEntryBytes = 32;

    public byte[] Write(FatBuildNode directory, uint parentCluster, bool isRoot, int size)
    {
        byte[] entries = new byte[size];
        int offset = 0;

        if (isRoot)
        {
            WriteShortEntry(entries.AsSpan(offset), Encoding.ASCII.GetBytes(label.ToUpperInvariant().PadRight(FatNames.ShortNameLength)), 0x08, 0, 0);
            offset += DirectoryEntryBytes;
        }
        else
        {
            WriteShortEntry(entries.AsSpan(offset), Encoding.ASCII.GetBytes(".".PadRight(FatNames.ShortNameLength)), 0x10, directory.FirstCluster, 0);
            WriteShortEntry(entries.AsSpan(offset + DirectoryEntryBytes), Encoding.ASCII.GetBytes("..".PadRight(FatNames.ShortNameLength)), 0x10, parentCluster, 0);
            offset += 2 * DirectoryEntryBytes;
        }

        foreach (FatBuildNode child in directory.Children)
        {
            if (child.HasLongName)
            {
                offset = WriteLongName(entries, offset, child.Name, FatNames.Checksum(child.ShortName));
            }

            WriteShortEntry(
                entries.AsSpan(offset),
                child.ShortName,
                child.IsDirectory ? (byte)0x10 : (byte)0x20,
                child.FirstCluster,
                child.IsDirectory ? 0 : (uint)child.Content!.Length);
            offset += DirectoryEntryBytes;
        }

        return entries;
    }

    // The long name's parts go in reverse, the last part first and marked with 0x40.
    private static int WriteLongName(byte[] entries, int offset, string name, byte checksum)
    {
        int count = FatNames.LongNameEntries(name);

        for (int order = count; order >= 1; order--)
        {
            Span<byte> entry = entries.AsSpan(offset, DirectoryEntryBytes);
            entry[0] = (byte)(order | (order == count ? 0x40 : 0));
            entry[11] = 0x0F;
            entry[13] = checksum;

            for (int index = 0; index < FatNames.LongNameCharacters; index++)
            {
                int position = ((order - 1) * FatNames.LongNameCharacters) + index;
                ushort value = position < name.Length ? name[position] : position == name.Length ? (ushort)0 : (ushort)0xFFFF;
                BinaryPrimitives.WriteUInt16LittleEndian(entry[FatNames.LongNameCharacterOffset(index)..], value);
            }

            offset += DirectoryEntryBytes;
        }

        return offset;
    }

    private void WriteShortEntry(Span<byte> entry, byte[] shortName, byte attributes, uint cluster, uint size)
    {
        shortName.CopyTo(entry);
        entry[11] = attributes;
        (ushort date, ushort time) = FatTimestamp();
        BinaryPrimitives.WriteUInt16LittleEndian(entry[14..], time);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[16..], date);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[18..], date);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[20..], (ushort)(cluster >> 16));
        BinaryPrimitives.WriteUInt16LittleEndian(entry[22..], time);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[24..], date);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[26..], (ushort)cluster);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[28..], size);
    }

    private (ushort Date, ushort Time) FatTimestamp()
    {
        DateTime at = timestamp.Year < 1980 ? new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc) : timestamp;

        return (
            (ushort)(((at.Year - 1980) << 9) | (at.Month << 5) | at.Day),
            (ushort)((at.Hour << 11) | (at.Minute << 5) | (at.Second / 2)));
    }
}
