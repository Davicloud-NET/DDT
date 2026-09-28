// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Server.Configuration;
using Microsoft.Extensions.Options;

namespace DDT.Server.Machines;

// Hashing the agent on every check would read it every time a machine boots. So the hash is kept until the file's
// length or write time changes. That's also how a replaced file is noticed without a restart.
public sealed class AgentReleaseStore(IOptions<AgentReleaseOptions> options, IOptions<DdtOptions> ddt)
{
    // ddt-agent.exe is about 11 MB. The limit leaves room for a debug build and keeps a stray upload from filling the store.
    public const long MaxBytes = 128L * 1024 * 1024;

    private readonly Lock _lock = new();
    private (long Length, DateTime WriteTimeUtc, AgentRelease Release)? _cached;

    public string BinaryPath => Path.GetFullPath(string.IsNullOrWhiteSpace(options.Value.BinaryPath)
        ? Path.Combine(ddt.Value.StorePath, "agent", "ddt-agent.exe")
        : options.Value.BinaryPath);

    public async Task<AgentRelease?> CurrentAsync(CancellationToken cancellationToken)
    {
        FileInfo file = new(BinaryPath);

        if (!file.Exists)
        {
            return null;
        }

        lock (_lock)
        {
            if (_cached is { } cached && cached.Length == file.Length && cached.WriteTimeUtc == file.LastWriteTimeUtc)
            {
                return cached.Release;
            }
        }

        byte[] hash;

        await using (FileStream stream = file.OpenRead())
        {
            hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        AgentRelease release = new(Convert.ToHexStringLower(hash), file.Length);

        lock (_lock)
        {
            _cached = (file.Length, file.LastWriteTimeUtc, release);
        }

        return release;
    }

    // Saves the agent an administrator uploads, and hashes it while writing.
    public async Task<(ReleaseUploadStatus Status, AgentRelease? Release)> SaveAsync(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        string path = BinaryPath;
        string temporary = FileReplacement.TemporaryFor(path);
        long size = 0;

        try
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] header = new byte[2];
            byte[] buffer = new byte[81920];

            await using (FileStream file = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true))
            {
                int read;

                while ((read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    for (int index = 0; index < read && size + index < header.Length; index++)
                    {
                        header[size + index] = buffer[index];
                    }

                    size += read;

                    if (size > MaxBytes)
                    {
                        return (ReleaseUploadStatus.TooLarge, null);
                    }

                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            // Every Windows executable starts with the DOS header's MZ.
            if (size < header.Length || header[0] != (byte)'M' || header[1] != (byte)'Z')
            {
                return (ReleaseUploadStatus.Refused, null);
            }

            string sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
            File.Move(temporary, path, overwrite: true);

            return (ReleaseUploadStatus.Saved, new AgentRelease(sha256, size));
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
