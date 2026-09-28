// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Facts;

// The firmware tables through kernel32, which needs no privilege for them.
public sealed class FirmwareTables : IFirmwareTables
{
    public const uint RawSmbiosProvider = NativeMethods.RawSmbiosProvider;
    public const uint AcpiProvider = NativeMethods.AcpiProvider;

    public byte[]? List(uint provider) =>
        Call(buffer => NativeMethods.EnumSystemFirmwareTables(provider, buffer, (uint)(buffer?.Length ?? 0)));

    public byte[]? Read(uint provider, uint id) =>
        Call(buffer => NativeMethods.GetSystemFirmwareTable(provider, id, buffer, (uint)(buffer?.Length ?? 0)));

    // Both functions answer a buffer that's too small with the size they need, and a failure with zero. Windows allows
    // the size to grow between the two calls, so it's asked again then.
    private static byte[]? Call(Func<byte[]?, uint> function)
    {
        uint size = function(null);

        for (int attempt = 0; attempt < 3 && size > 0; attempt++)
        {
            byte[] buffer = new byte[size];
            uint written = function(buffer);

            if (written <= size)
            {
                return written == 0 ? null : buffer[..(int)written];
            }

            size = written;
        }

        return null;
    }
}
