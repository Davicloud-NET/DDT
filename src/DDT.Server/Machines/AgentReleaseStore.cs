// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Settings;
using DDT.Server.Configuration;
using Microsoft.Extensions.Options;

namespace DDT.Server.Machines;

// Machines get the file configuration names, else the uploaded agent, else the one the server came with. Hashing an
// agent on every check would read it every time a machine boots. So the hash is kept until the file's length or write
// time changes. That's also how a replaced file is noticed without a restart.
public sealed class AgentReleaseStore(IOptions<AgentReleaseOptions> options, IOptions<DdtOptions> ddt, BundledReleases bundled)
{
    // ddt-agent.exe is about 11 MB. The limit leaves room for a debug build and keeps a stray upload from filling the store.
    public const long MaxBytes = 128L * 1024 * 1024;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, (long Length, DateTime WriteTimeUtc, StoredAgent Agent)> _cached = new(StringComparer.Ordinal);

    // True when configuration names its own file. An upload wouldn't replace that file.
    public bool Configured => !string.IsNullOrWhiteSpace(options.Value.BinaryPath);

    // The file configuration names, or where an upload goes
    public string BinaryPath => Path.GetFullPath(Configured
        ? options.Value.BinaryPath
        : Path.Combine(ddt.Value.StorePath, "agent", "ddt-agent.exe"));

    public async Task<AgentRelease?> CurrentAsync(CancellationToken cancellationToken) =>
        (await OfferedAsync(cancellationToken).ConfigureAwait(false))?.Release;

    // A configured file that's missing offers nothing: it's named for a reason.
    public async Task<StoredAgent?> OfferedAsync(CancellationToken cancellationToken) =>
        await DescribeAsync(BinaryPath, Configured ? AgentBinarySource.Configuration : AgentBinarySource.Uploaded, cancellationToken).ConfigureAwait(false)
            ?? (Configured ? null : await BundledAsync(cancellationToken).ConfigureAwait(false));

    public Task<StoredAgent?> BundledAsync(CancellationToken cancellationToken) =>
        DescribeAsync(bundled.AgentPath, AgentBinarySource.Bundled, cancellationToken);

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
            FileReplacement.Replace(temporary, path);

            return (ReleaseUploadStatus.Saved, new AgentRelease(sha256, size));
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    // Machines then get the agent the server came with, or keep their boot image's. False when nothing was uploaded.
    public bool RemoveUpload()
    {
        if (Configured || !File.Exists(BinaryPath))
        {
            return false;
        }

        FileReplacement.Delete(BinaryPath);

        return true;
    }

    private async Task<StoredAgent?> DescribeAsync(string path, AgentBinarySource source, CancellationToken cancellationToken)
    {
        FileInfo file = new(path);

        if (!file.Exists)
        {
            return null;
        }

        lock (_lock)
        {
            if (_cached.TryGetValue(path, out var cached) && cached.Length == file.Length && cached.WriteTimeUtc == file.LastWriteTimeUtc)
            {
                return cached.Agent;
            }
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ExecutableVersion version = new();
        byte[] buffer = new byte[81920];
        int read;

        try
        {
            await using FileStream stream = FileReplacement.OpenRead(path);

            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                version.Append(buffer.AsSpan(0, read));
            }
        }
        catch (FileNotFoundException)
        {
            // Removed since the check above
            return null;
        }

        StoredAgent agent = new(path, new AgentRelease(Convert.ToHexStringLower(hash.GetHashAndReset()), file.Length), version.Found, source);

        lock (_lock)
        {
            _cached[path] = (file.Length, file.LastWriteTimeUtc, agent);
        }

        return agent;
    }
}
