// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;

namespace DDT.Server.Applications.Installers;

// Finds a resource in the bytes of the section that holds the resource directory: type, then name, then the first
// language.
internal static class PeResourceDirectory
{
    private const uint SubdirectoryFlag = 0x80000000;

    // Where the resource's bytes are in the section, or null. A null name takes the first.
    public static (int Start, int Length)? Find(ReadOnlySpan<byte> section, PeSection layout, uint resourceRva, uint type, uint? name)
    {
        ArgumentNullException.ThrowIfNull(layout);

        int directory = (int)(resourceRva - layout.VirtualAddress);

        if (resourceRva < layout.VirtualAddress || directory >= section.Length
            || Child(section, directory, directory, type) is not { } names
            || Child(section, directory, names, name) is not { } languages
            || Child(section, directory, languages, null) is not { } data
            || data + 8 > section.Length)
        {
            return null;
        }

        uint rva = BinaryPrimitives.ReadUInt32LittleEndian(section[data..]);
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(section[(data + 4)..]);
        long start = (long)rva - layout.VirtualAddress;

        return rva < layout.VirtualAddress || start + length > section.Length ? null : ((int)start, (int)length);
    }

    // The offset an entry of the resource directory at offset points to: the one with the id, or else the first.
    private static int? Child(ReadOnlySpan<byte> section, int root, int offset, uint? id)
    {
        if (offset < 0 || offset + 16 > section.Length)
        {
            return null;
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(section[(offset + 12)..]) + BinaryPrimitives.ReadUInt16LittleEndian(section[(offset + 14)..]);

        for (int index = 0; index < count && offset + 16 + (index * 8) + 8 <= section.Length; index++)
        {
            ReadOnlySpan<byte> entry = section[(offset + 16 + (index * 8))..];
            uint name = BinaryPrimitives.ReadUInt32LittleEndian(entry);

            if (id is null || name == id)
            {
                long target = root + (long)(BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]) & ~SubdirectoryFlag);

                return target < section.Length ? (int)target : null;
            }
        }

        return null;
    }
}
