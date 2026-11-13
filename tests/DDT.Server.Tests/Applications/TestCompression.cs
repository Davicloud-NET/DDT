// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using SharpCompress.Compressors.LZMA;

namespace DDT.Server.Tests.Applications;

// Compression as installers store it: LZMA's five property bytes then the raw stream, or deflate without zlib framing.
internal static class TestCompression
{
    public static byte[] Lzma(byte[] data)
    {
        using MemoryStream output = new();
        byte[] properties;

        using (LzmaStream lzma = LzmaStream.Create(new LzmaEncoderProperties(eos: false, 1 << 16), false, output))
        {
            lzma.Write(data);
            properties = lzma.Properties;
        }

        return [.. properties, .. output.ToArray()];
    }

    public static byte[] Deflate(byte[] data)
    {
        using MemoryStream output = new();

        using (DeflateStream deflate = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data);
        }

        return output.ToArray();
    }
}
