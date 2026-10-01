// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Settings;
using DDT.Server.Configuration;
using Microsoft.Extensions.Options;

namespace DDT.Server.Machines;

// The console is kept as one zip, so an upload replaces it with a single rename and a machine never sees half of one.
// Like AgentReleaseStore, it offers the configured zip, else the uploaded one, else the one the server came with, and
// hashes the files once and keeps the hashes until the zip's length or write time changes.
public sealed class ConsoleReleaseStore(IOptions<AgentReleaseOptions> options, IOptions<DdtOptions> ddt, BundledReleases bundled)
{
    // The console's three files are about 29 MB, and zipped about 12 MB. The agent's limit holds for the zip and for what
    // it unpacks to, so a small zip cannot fill the store either.
    public const long MaxBytes = AgentReleaseStore.MaxBytes;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, (long Length, DateTime WriteTimeUtc, StoredConsole? Console)> _cached = new(StringComparer.Ordinal);

    // True when configuration names its own zip. An upload wouldn't replace that file.
    public bool Configured => !string.IsNullOrWhiteSpace(options.Value.ConsolePath);

    // The zip configuration names, or where an upload goes
    public string PackagePath => Path.GetFullPath(Configured
        ? options.Value.ConsolePath
        : Path.Combine(ddt.Value.StorePath, "agent", "ddt-console.zip"));

    // Null if there's no console, or the zip isn't a valid one. Machines then keep the console from their boot image.
    public async Task<ConsoleRelease?> CurrentAsync(CancellationToken cancellationToken) =>
        (await OfferedAsync(cancellationToken).ConfigureAwait(false))?.Release;

    // An upload that isn't a console hides the one the server came with, as a configured one does: someone put it there.
    public async Task<StoredConsole?> OfferedAsync(CancellationToken cancellationToken) =>
        Configured || File.Exists(PackagePath)
            ? await DescribeAsync(PackagePath, Configured ? AgentBinarySource.Configuration : AgentBinarySource.Uploaded, cancellationToken).ConfigureAwait(false)
            : await BundledAsync(cancellationToken).ConfigureAwait(false);

    public Task<StoredConsole?> BundledAsync(CancellationToken cancellationToken) =>
        DescribeAsync(bundled.ConsolePath, AgentBinarySource.Bundled, cancellationToken);

    // Writes one of the console's files as the zip holds it now. If an upload happens in between, the download won't
    // match the release, and the agent refuses it.
    public static async Task CopyFileAsync(StoredConsole console, string name, Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(destination);

        await using FileStream stream = FileReplacement.OpenRead(console.Path);
        await using ZipArchive archive = await ZipArchive.CreateAsync(stream, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: null, cancellationToken)
            .ConfigureAwait(false);

        ZipArchiveEntry entry = archive.GetEntry(name) ?? throw new FileNotFoundException($"The console has no {name}.");
        await using Stream content = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
        await content.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    // The release of a zip that holds exactly the console's files at its root, or null.
    public static async Task<ConsoleRelease?> ReadAsync(Stream zip, CancellationToken cancellationToken) =>
        (await ReadWithVersionAsync(zip, cancellationToken).ConfigureAwait(false)).Release;

    // Machines then get the console the server came with, or keep their boot image's. False when nothing was uploaded.
    public bool RemoveUpload()
    {
        if (Configured || !File.Exists(PackagePath))
        {
            return false;
        }

        FileReplacement.Delete(PackagePath);

        return true;
    }

    private async Task<StoredConsole?> DescribeAsync(string path, AgentBinarySource source, CancellationToken cancellationToken)
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
                return cached.Console;
            }
        }

        StoredConsole? console;

        try
        {
            await using FileStream stream = FileReplacement.OpenRead(path);
            (ConsoleRelease? release, Version? version) = await ReadWithVersionAsync(stream, cancellationToken).ConfigureAwait(false);
            console = release is null ? null : new StoredConsole(path, release, version, source);
        }
        catch (FileNotFoundException)
        {
            // Removed since the check above
            return null;
        }

        lock (_lock)
        {
            _cached[path] = (file.Length, file.LastWriteTimeUtc, console);
        }

        return console;
    }

    // The version is that of ddt-console.exe, the first of the console's files.
    private static async Task<(ConsoleRelease? Release, Version? Version)> ReadWithVersionAsync(Stream zip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(zip);

        try
        {
            await using ZipArchive archive = await ZipArchive.CreateAsync(zip, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null, cancellationToken)
                .ConfigureAwait(false);

            if (archive.Entries.Count != ConsoleRelease.FileNames.Count)
            {
                return (null, null);
            }

            List<ConsoleReleaseFile> files = [];
            ExecutableVersion version = new();
            byte[] buffer = new byte[81920];

            foreach (string name in ConsoleRelease.FileNames)
            {
                if (archive.GetEntry(name) is not { } entry)
                {
                    return (null, null);
                }

                using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using Stream content = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
                int read;

                while ((read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    hash.AppendData(buffer, 0, read);

                    if (files.Count == 0)
                    {
                        version.Append(buffer.AsSpan(0, read));
                    }
                }

                files.Add(new ConsoleReleaseFile(name, Convert.ToHexStringLower(hash.GetHashAndReset()), entry.Length));
            }

            return (new ConsoleRelease(files), version.Found);
        }
        catch (InvalidDataException)
        {
            return (null, null);
        }
    }

    // Accepts the files at the zip's root or in one folder, which is what zipping the folder gives. It's stored with
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

            FileReplacement.Replace(stored, path);

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

    // The unpacked size of all files together counts against MaxBytes.
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

    // The console's files by name, at the root or all in one folder. Null if the zip holds anything else or misses one.
    private static Dictionary<string, ZipArchiveEntry>? ConsoleFiles(ZipArchive archive)
    {
        Dictionary<string, ZipArchiveEntry> files = new(StringComparer.OrdinalIgnoreCase);
        string? folder = null;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string path = entry.FullName.Replace('\\', '/');

            // Folders have their own entries, ending in a slash.
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
}
