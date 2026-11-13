// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using DDT.Core;

namespace DDT.Server.Applications.Installers;

// Inno Setup's TSetupLdrOffsetTable in resource 11111 of the loader (Shared.Struct.pas): where setup-0.bin, the setup
// data, starts. Version 1 has 32-bit fields (5.1.5 to 6.4), version 2 64-bit ones in a Delphi record (6.5 on).
internal static class InnoLoaderTable
{
    public const uint ResourceType = 10;
    public const uint ResourceName = 11111;

    private const int IdLength = 12;
    private const int VersionOneLength = 44;
    private const int VersionTwoLength = 64;

    // innoextract's offsets.cpp knows both IDs from 5.1.5 on.
    private static readonly byte[] s_id = [(byte)'r', (byte)'D', (byte)'l', (byte)'P', (byte)'t', (byte)'S', 0xCD, 0xE6, 0xD7, 0x7B, 0x0B, 0x2A];
    private static readonly byte[] s_otherId = [(byte)'n', (byte)'S', (byte)'5', (byte)'W', (byte)'7', (byte)'d', (byte)'T', 0x83, 0xAA, 0x1B, 0x0F, (byte)'j'];

    // The file offset of setup-0.bin, or null for a table that is not one or whose CRC does not match.
    public static long? Offset0(ReadOnlySpan<byte> table)
    {
        if (table.Length < IdLength + 4 || !(table[..IdLength].SequenceEqual(s_id) || table[..IdLength].SequenceEqual(s_otherId)))
        {
            return null;
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(table[IdLength..]) switch
        {
            1 when table.Length >= VersionOneLength && Matches(table, VersionOneLength) => BinaryPrimitives.ReadUInt32LittleEndian(table[32..]),
            2 when table.Length >= VersionTwoLength && Matches(table, VersionTwoLength) => Positive(BinaryPrimitives.ReadInt64LittleEndian(table[40..])),
            _ => null,
        };
    }

    // TableCRC is the last field, over all before it.
    private static bool Matches(ReadOnlySpan<byte> table, int length) =>
        Crc32.Append(0, table[..(length - 4)]) == BinaryPrimitives.ReadUInt32LittleEndian(table[(length - 4)..]);

    private static long? Positive(long offset) => offset > 0 ? offset : null;
}
