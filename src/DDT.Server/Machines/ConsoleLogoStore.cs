// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Security.Cryptography;
using DDT.Server.Configuration;
using Microsoft.Extensions.Options;

namespace DDT.Server.Machines;

// The PNG logo the console at the machine shows. Every registration names its hash, so the hash is kept until the
// file's length or write time changes.
public sealed class ConsoleLogoStore(IOptions<DdtOptions> ddt)
{
    // A logo is a small picture: the console shows it at most 32 pixels high and 200 wide.
    public const int MaxBytes = 512 * 1024;
    public const int MaxDimension = 2048;

    private static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly Lock _lock = new();
    private (long Length, DateTime WriteTimeUtc, ConsoleLogoFile? Logo)? _cached;

    public string Path => System.IO.Path.GetFullPath(System.IO.Path.Combine(ddt.Value.StorePath, "console", "logo.png"));

    public async Task<ConsoleLogoFile?> CurrentAsync(CancellationToken cancellationToken)
    {
        FileInfo file = new(Path);

        if (!file.Exists)
        {
            return null;
        }

        lock (_lock)
        {
            if (_cached is { } cached && cached.Length == file.Length && cached.WriteTimeUtc == file.LastWriteTimeUtc)
            {
                return cached.Logo;
            }
        }

        byte[] content = await File.ReadAllBytesAsync(file.FullName, cancellationToken).ConfigureAwait(false);
        ConsoleLogoFile? logo = Describe(content);

        lock (_lock)
        {
            _cached = (file.Length, file.LastWriteTimeUtc, logo);
        }

        return logo;
    }

    // Written next to the logo it replaces and renamed over it, so an agent never downloads half of one.
    public async Task SaveAsync(byte[] png, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(png);

        string path = Path;
        string temporary = FileReplacement.TemporaryFor(path);

        try
        {
            await File.WriteAllBytesAsync(temporary, png, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public void Delete() => File.Delete(Path);

    // Null unless the bytes are a PNG: the signature, IHDR first with at least one pixel, whole chunks, IEND last. Only
    // the structure is checked; the console's decoder reads the pixels.
    public static ConsoleLogoFile? Describe(ReadOnlySpan<byte> png)
    {
        if (!png.StartsWith(Signature))
        {
            return null;
        }

        int offset = Signature.Length;
        int width = 0;
        int height = 0;
        bool first = true;

        while (offset + 12 <= png.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(png[offset..]);
            ReadOnlySpan<byte> type = png.Slice(offset + 4, 4);

            if (length > (uint)(png.Length - offset - 12))
            {
                return null;
            }

            ReadOnlySpan<byte> data = png.Slice(offset + 8, (int)length);

            if (first)
            {
                if (!type.SequenceEqual("IHDR"u8) || length != 13)
                {
                    return null;
                }

                width = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data), int.MaxValue);
                height = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data[4..]), int.MaxValue);
                first = false;
            }

            offset += 12 + (int)length;

            if (type.SequenceEqual("IEND"u8))
            {
                return offset == png.Length && width > 0 && height > 0
                    ? new ConsoleLogoFile(Convert.ToHexStringLower(SHA256.HashData(png)), png.Length, width, height)
                    : null;
            }
        }

        return null;
    }
}
