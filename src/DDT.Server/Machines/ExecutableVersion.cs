// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;

namespace DDT.Server.Machines;

// The file version of a Windows executable, found while its bytes pass by for hashing. FileVersionInfo reads only
// managed assemblies on Linux, and the agent is native. So this looks for the fixed part of the version resource,
// VS_FIXEDFILEINFO: its signature, its structure version, then the file version.
public sealed class ExecutableVersion
{
    private const int VersionBytes = 8;

    private static readonly byte[] s_signature = [0xBD, 0x04, 0xEF, 0xFE, 0x00, 0x00, 0x01, 0x00];

    // The end of the last block, where a signature may have begun
    private byte[] _carry = [];

    public Version? Found { get; private set; }

    public static Version? Of(ReadOnlySpan<byte> executable)
    {
        ExecutableVersion version = new();
        version.Append(executable);

        return version.Found;
    }

    public void Append(ReadOnlySpan<byte> block)
    {
        if (Found is not null)
        {
            return;
        }

        byte[] joined = [.. _carry, .. block];
        int at = joined.AsSpan().IndexOf(s_signature);

        if (at >= 0 && at + s_signature.Length + VersionBytes <= joined.Length)
        {
            ReadOnlySpan<byte> version = joined.AsSpan(at + s_signature.Length, VersionBytes);
            uint high = BinaryPrimitives.ReadUInt32LittleEndian(version);
            uint low = BinaryPrimitives.ReadUInt32LittleEndian(version[4..]);
            Found = new Version((int)(high >> 16), (int)(high & 0xFFFF), (int)(low >> 16), (int)(low & 0xFFFF));

            return;
        }

        // From a signature that's cut off, or else the last bytes one could start in
        int keep = at >= 0 ? joined.Length - at : Math.Min(joined.Length, s_signature.Length + VersionBytes - 1);
        _carry = joined[^keep..];
    }
}
