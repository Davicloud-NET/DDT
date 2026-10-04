// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Security.Cryptography;

namespace DDT.Server.Tests;

// Stand-ins for Windows executables: the MZ the server asks for, and the fixed part of a version resource.
internal static class Executables
{
    public static byte[] Versioned(string version, int size = 4096) =>
        [(byte)'M', (byte)'Z', .. RandomNumberGenerator.GetBytes(size), .. FixedFileInfo(Version.Parse(version)), .. RandomNumberGenerator.GetBytes(64)];

    // VS_FIXEDFILEINFO up to its file version
    public static byte[] FixedFileInfo(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        byte[] info = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(info, 0xFEEF04BD);
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(4), 0x00010000);
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(8), ((uint)version.Major << 16) | (uint)version.Minor);
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(12), ((uint)version.Build << 16) | (uint)Math.Max(version.Revision, 0));

        return info;
    }
}
