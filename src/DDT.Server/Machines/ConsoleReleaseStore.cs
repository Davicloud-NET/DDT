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
    // The console's three files are about 29 MB, and zipped about 12 MB. The agent's limit holds for the zip and for what
    // it unpacks to, so a small zip cannot fill the store either.
    public const long MaxBytes = AgentReleaseStore.MaxBytes;

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

    // Taken whether the files are at the zip's root or in one folder, as zipping the folder makes it, and stored with
    // exactly the console's files at the root.
    public async Task<(ReleaseUploadStatus Status, ConsoleRelease? Release)> SaveAsync(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        string path = PackagePath;
        string received = FileReplacement.TemporaryFor(path);
        string stored = FileReplacement.TemporaryFor(path);

        try
        {
            await using (FileStream file = new(received, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                if (await CopyAtMostAsync(content, file, MaxBytes, cancellationToken).ConfigureAwait(false) < 0)
                {
                    return (ReleaseUploadStatus.TooLarge, null);
                }
            }

            ReleaseUploadStatus repacked = await RepackAsync(received, stored, cancellationToken).ConfigureAwait(false);

            if (repacked != ReleaseUploadStatus.Saved)
            {
                return (repacked, null);
            }

            ConsoleRelease release;

            await using (FileStream file = new(stored, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            {
                release = await ReadAsync(file, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The repacked console is not one.");
            }

            File.Move(stored, path, overwrite: true);

            return (ReleaseUploadStatus.Saved, release);
        }
        finally
        {
            File.Delete(received);
            File.Delete(stored);
        }
    }

    // Copies the console's files from the zip received into a new one, and checks that each is a Windows executable.
    private static async Task<ReleaseUploadStatus> RepackAsync(string received, string stored, CancellationToken cancellationToken)
    {
        await using FileStream source = new(received, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        ZipArchive input;

        try
        {
            input = await ZipArchive.CreateAsync(source, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return ReleaseUploadStatus.Refused;
        }

        await using (input)
        {
            if (ConsoleFiles(input) is not { } files)
            {
                return ReleaseUploadStatus.Refused;
            }

            await using FileStream target = new(stored, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true);
            await using ZipArchive output = await ZipArchive.CreateAsync(target, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken)
                .ConfigureAwait(false);

            return await CopyExecutablesAsync(files, output, cancellationToken).ConfigureAwait(false);
        }
    }

    // What the files unpack to counts against MaxBytes together.
    private static async Task<ReleaseUploadStatus> CopyExecutablesAsync(
        Dictionary<string, ZipArchiveEntry> files,
        ZipArchive output,
        CancellationToken cancellationToken)
    {
        long left = MaxBytes;

        foreach (string name in ConsoleRelease.FileNames)
        {
            ZipArchiveEntry entry = output.CreateEntry(name, CompressionLevel.Optimal);
            byte[] header = new byte[2];
            long copied;

            try
            {
                await using Stream from = await files[name].OpenAsync(cancellationToken).ConfigureAwait(false);
                await using Stream to = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
                await from.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);

                // Every Windows executable and library starts with the DOS header's MZ.
                if (header[0] != (byte)'M' || header[1] != (byte)'Z')
                {
                    return ReleaseUploadStatus.Refused;
                }

                await to.WriteAsync(header, cancellationToken).ConfigureAwait(false);
                copied = await CopyAtMostAsync(from, to, left - header.Length, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                return ReleaseUploadStatus.Refused;
            }

            if (copied < 0)
            {
                return ReleaseUploadStatus.TooLarge;
            }

            left -= header.Length + copied;
        }

        return ReleaseUploadStatus.Saved;
    }

    // The console's files by name, whether at the root or all in one folder, or null when the zip holds anything else or
    // misses one.
    private static Dictionary<string, ZipArchiveEntry>? ConsoleFiles(ZipArchive archive)
    {
        Dictionary<string, ZipArchiveEntry> files = new(StringComparer.OrdinalIgnoreCase);
        string? folder = null;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string path = entry.FullName.Replace('\\', '/');

            // Folders are entries of their own, ending in a slash.
            if (path.EndsWith('/'))
            {
                continue;
            }

            int slash = path.LastIndexOf('/');
            string directory = slash < 0 ? string.Empty : path[..slash];
            folder ??= directory;

            string? name = ConsoleRelease.FileNames.FirstOrDefault(known => known.Equals(path[(slash + 1)..], StringComparison.OrdinalIgnoreCase));

            if (directory != folder || directory.Contains('/', StringComparison.Ordinal) || name is null || !files.TryAdd(name, entry))
            {
                return null;
            }
        }

        return files.Count == ConsoleRelease.FileNames.Count ? files : null;
    }

    // The number of bytes copied, or -1 when there were more than limit.
    private static async Task<long> CopyAtMostAsync(Stream source, Stream destination, long limit, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[81920];
        long size = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            size += read;

            if (size > limit)
            {
                return -1;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return size;
    }

    // Shared for deletion, so an upload can rename the new zip over one that is being read.
    private static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, useAsync: true);
}
