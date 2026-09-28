// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers;
using System.Security.Cryptography;
using DDT.Server.Data;

namespace DDT.Server.Images;

// The files of the library, each stored once by its hash, and the part files uploads arrive in.
internal static class LibraryFiles
{
    private const int HashBufferBytes = 1024 * 1024;

    // Puts source, the part file or a disk image's compressed copy, into the library and saves its rows. The file
    // decides, not its rows: a stored file whose rows were lost is used again, and one missing under its rows is put
    // back. Call with LibraryLock held.
    public static async Task SaveWithFileAsync(ImageStore store, DdtDbContext database, string source, string sha256)
    {
        string target = store.ObjectPath(sha256);
        bool moved = !File.Exists(target);

        if (moved)
        {
            Directory.CreateDirectory(store.ObjectsDirectory);
            File.Move(source, target, overwrite: false);
        }

        try
        {
            // The file is in the library by now, so its rows are saved even while the server stops.
            await database.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch when (moved)
        {
            // Completing again, as the failure's answer asks, starts from the part file.
            File.Move(target, source, overwrite: false);

            throw;
        }

        if (!moved)
        {
            File.Delete(source);
        }
    }

    public static async Task<string> HashAsync(FileStream stream, CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(HashBufferBytes);

        try
        {
            int count;

            while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, count);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    // A WIM starts with its magic, a pipable one with its own, which the WIM reader refuses by name. Any other image
    // upload is a disk image, or refused as neither.
    public static async Task<bool> IsWimAsync(string part, CancellationToken cancellationToken)
    {
        byte[] magic = new byte[8];

        await using FileStream file = new(part, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 0, useAsync: true);

        return await file.ReadAtLeastAsync(magic, magic.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false) == magic.Length
            && (magic.AsSpan().SequenceEqual("MSWIM\0\0\0"u8) || magic.AsSpan().SequenceEqual("WLPWM\0\0\0"u8));
    }
}
