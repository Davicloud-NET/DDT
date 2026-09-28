// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;

namespace DDT.Agent;

// Files the server offers by SHA-256 and size, such as a newer agent, its console's files and the console's logo.
internal static class ReleaseFiles
{
    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        return Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
    }

    public static async Task<bool> HoldsAsync(string path, string sha256, long size, CancellationToken cancellationToken) =>
        File.Exists(path)
        && new FileInfo(path).Length == size
        && string.Equals(await Sha256Async(path, cancellationToken).ConfigureAwait(false), sha256, StringComparison.OrdinalIgnoreCase);

    // Downloads into path.part first: a truncated download or a file replaced on the server in between must never be
    // started.
    public static async Task FetchAsync(
        string path,
        string sha256,
        long size,
        Func<Stream, CancellationToken, Task> download,
        CancellationToken cancellationToken)
    {
        string partial = path + ".part";

        await using (FileStream file = new(partial, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await download(file, cancellationToken).ConfigureAwait(false);
        }

        if (!await HoldsAsync(partial, sha256, size, cancellationToken).ConfigureAwait(false))
        {
            File.Delete(partial);

            throw new InvalidDataException("the download does not match what the server announced");
        }

        File.Move(partial, path, overwrite: true);
    }
}
