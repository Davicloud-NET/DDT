// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using SharpCompress.Compressors.LZMA;

namespace DDT.Server.Applications.Installers;

// The raw LZMA stream Inno Setup and NSIS write: a properties byte, the dictionary size, then the data, no length.
internal static class InstallerLzma
{
    private const int MaxProperties = 9 * 5 * 5;
    private const uint MinDictionary = 4096;

    // Null for no LZMA properties. The dictionary is capped at maxOutput, the most a caller reads; no output size, as
    // SharpCompress fails data that runs on past it (NSIS's solid stream).
    public static Stream? Open(Stream input, long maxOutput)
    {
        ArgumentNullException.ThrowIfNull(input);

        byte[] properties = new byte[5];

        if (input.ReadAtLeast(properties, properties.Length, throwOnEndOfStream: false) != properties.Length || properties[0] > MaxProperties)
        {
            return null;
        }

        uint dictionary = BinaryPrimitives.ReadUInt32LittleEndian(properties.AsSpan(1));
        BinaryPrimitives.WriteUInt32LittleEndian(properties.AsSpan(1), (uint)Math.Clamp(dictionary, MinDictionary, Math.Max(MinDictionary, maxOutput)));

        return LzmaStream.Create(properties, input, -1, -1, leaveOpen: true);
    }
}
