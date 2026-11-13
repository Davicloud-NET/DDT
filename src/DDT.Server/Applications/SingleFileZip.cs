// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;

namespace DDT.Server.Applications;

// Zips one uploaded file. A fixed time and no compression make the same file give the same zip, so it is stored once,
// and an installer of gigabytes is copied, not compressed again.
internal static class SingleFileZip
{
    private static readonly DateTimeOffset s_entryTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task WriteAsync(string source, string target, string entryName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(entryName);

        await using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        await using FileStream output = new(target, FileMode.Create, FileAccess.ReadWrite, FileShare.None, bufferSize: 81920, useAsync: true);

        using ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true);
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
        entry.LastWriteTime = s_entryTime;
        entry.ExternalAttributes = 0;

        await using Stream data = entry.Open();
        await input.CopyToAsync(data, cancellationToken).ConfigureAwait(false);
    }
}
