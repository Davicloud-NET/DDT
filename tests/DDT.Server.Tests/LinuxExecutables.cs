// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace DDT.Server.Tests;

// Stand-ins for the agent for Linux: the ELF header the server asks for, and the marker its build writes.
internal static class LinuxExecutables
{
    private const ushort MachineX64 = 62;

    public static byte[] Versioned(string version, int size = 4096) =>
        [.. Header(), .. RandomNumberGenerator.GetBytes(size), .. Marker(version), .. RandomNumberGenerator.GetBytes(64)];

    public static byte[] Marker(string version) => Encoding.ASCII.GetBytes($"DDT-AGENT-VERSION:{version}\n");

    // The first 20 bytes of an ELF file. The defaults are a position-independent 64-bit program for x86-64.
    public static byte[] Header(byte bits = 2, ushort type = 3, ushort machine = MachineX64)
    {
        byte[] header = new byte[20];
        "\u007fELF"u8.CopyTo(header);
        header[4] = bits;
        header[5] = 1;
        header[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(16), type);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(18), machine);

        return header;
    }
}
