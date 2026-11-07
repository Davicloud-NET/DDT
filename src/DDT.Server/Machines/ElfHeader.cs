// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;

namespace DDT.Server.Machines;

// The first bytes of an ELF file, which say what the file is and which processor it is for.
public static class ElfHeader
{
    // Up to e_machine, the last field read here
    public const int Length = 20;

    private const byte Class64 = 2;
    private const byte LittleEndian = 1;
    private const ushort Executable = 2;

    // What a position-independent program is, as the agent is one
    private const ushort SharedObject = 3;
    private const ushort MachineX64 = 62;

    // A 64-bit program for x86-64. Not a library's object file, a core dump, or a program for another processor.
    public static bool IsX64Program(ReadOnlySpan<byte> header)
    {
        if (header.Length < Length || !header.StartsWith("\u007fELF"u8) || header[4] != Class64 || header[5] != LittleEndian)
        {
            return false;
        }

        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(header[16..]);

        return type is Executable or SharedObject && BinaryPrimitives.ReadUInt16LittleEndian(header[18..]) == MachineX64;
    }
}
