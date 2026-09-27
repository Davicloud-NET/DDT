// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Server.Configuration;
using Microsoft.Extensions.Options;

namespace DDT.Server.Machines;

// The console is kept as one zip, so an upload replaces it with a single rename and a machine never sees half of one.
// Its files are hashed once and kept until the zip's length or write time changes, as AgentReleaseStore does.
public sealed class ConsoleReleaseStore(IOptions<AgentReleaseOptions> options, IOptions<DdtOptions> ddt)
{
    private readonly Lock _lock = new();
    private (long Length, DateTime WriteTimeUtc, ConsoleRelease? Release)? _cached;

    public string PackagePath => Path.GetFullPath(string.IsNullOrWhiteSpace(options.Value.ConsolePath)
        ? Path.Combine(ddt.Value.StorePath, "agent", "ddt-console.zip")
        : options.Value.ConsolePath);

    // Null when there is no console, or when the zip is not one, which leaves machines with the console of their boot
    // image.
    public async Task<ConsoleRelease?> CurrentAsync(CancellationToken cancellationToken)
    {
        FileInfo file = new(PackagePath);

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

        ConsoleRelease? release;

        await using (FileStream stream = Open(file.FullName))
        {
            release = await ReadAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        lock (_lock)
        {
            _cached = (file.Length, file.LastWriteTimeUtc, release);
        }

        return release;
    }

    // Writes one of the console's files as the zip holds it now. An upload in between shows as a download that does not
    // match the release, which the agent refuses.
    public async Task CopyFileAsync(string name, Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        await using FileStream stream = Open(PackagePath);
        await using ZipArchive archive = await ZipArchive.CreateAsync(stream, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: null, cancellationToken)
            .ConfigureAwait(false);

        ZipArchiveEntry entry = archive.GetEntry(name) ?? throw new FileNotFoundException($"The console has no {name}.");
        await using Stream content = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
        await content.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    // The release of a zip that holds exactly the console's files at its root, or null.
    public static async Task<ConsoleRelease?> ReadAsync(Stream zip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(zip);

        try
        {
            await using ZipArchive archive = await ZipArchive.CreateAsync(zip, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null, cancellationToken)
                .ConfigureAwait(false);

            if (archive.Entries.Count != ConsoleRelease.FileNames.Count)
            {
                return null;
            }

            List<ConsoleReleaseFile> files = [];

            foreach (string name in ConsoleRelease.FileNames)
            {
                if (archive.GetEntry(name) is not { } entry)
                {
                    return null;
                }

                await using Stream content = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
                byte[] hash = await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false);
                files.Add(new ConsoleReleaseFile(name, Convert.ToHexStringLower(hash), entry.Length));
            }

            return new ConsoleRelease(files);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    // Shared for deletion, so an upload can rename the new zip over one that is being read.
    private static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, useAsync: true);
}
