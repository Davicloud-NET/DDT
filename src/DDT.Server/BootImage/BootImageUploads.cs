// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.IO.Compression;
using DDT.Contracts.BootImage;
using DDT.Contracts.Messages;
using DDT.Pxe;

namespace DDT.Server.BootImage;

// Takes the build a builder made on another PC: a zip of its Boot, EFI and x64 folders. What it holds is checked
// before any of it is served, and it then becomes the current build as one built on the server does.
public sealed class BootImageUploads(BootImageCatalog catalog, CurrentBootImageJob job, BootImagePushes pushes, TimeProvider timeProvider)
{
    // A boot image is 300 to 600 MB
    public const long MaxBytes = 2L * 1024 * 1024 * 1024;

    private const long MaxUnpackedBytes = 2 * MaxBytes;
    private const int MaxFiles = 4096;

    private static readonly string[] s_folders = ["Boot/", "EFI/", "x64/"];
    private static readonly string[] s_required = ["Boot/boot.wim", "Boot/BCD", "Boot/boot.sdi", $"Boot/{BootImageCatalog.ManifestName}", "x64/bootmgfw.efi"];
    private static readonly byte[] s_wim = "MSWIM\0\0\0"u8.ToArray();

    // Returns the build's name, or why the upload was refused. Shown on the page as a job, like a build.
    public async Task<(string? Name, ServerMessage? Refusal)> ReceiveAsync(Stream body, BuilderToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(token);

        string name = timeProvider.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        if (!job.TryBegin(BootImageJobKind.Upload, token.IssuedBy, timeProvider.GetUtcNow()))
        {
            return (null, ServerMessages.BootImageBusy.With());
        }

        ServerMessage? refusal = ServerMessages.BootImageUploadBroken.With();

        try
        {
            await pushes.ViewChangedAsync(cancellationToken).ConfigureAwait(false);
            job.Append("Receiving a boot image from a builder.");
            refusal = await StoreAsync(body, name, cancellationToken).ConfigureAwait(false);

            if (refusal is null)
            {
                job.Append($"The server serves this build, {name}, from now on.");
            }
        }
        finally
        {
            job.End(refusal?.Text, timeProvider.GetUtcNow());

            if (job.TakeUnpushed() is { } output)
            {
                pushes.Output(output);
            }

            await pushes.ViewChangedAsync(CancellationToken.None).ConfigureAwait(false);
        }

        return refusal is null ? (name, null) : (null, refusal);
    }

    private async Task<ServerMessage?> StoreAsync(Stream body, string name, CancellationToken cancellationToken)
    {
        // Below builds, so the move into place stays on one volume. BootBuilds lists no name that starts with a dot.
        string work = Path.Combine(catalog.BootDirectory, BootBuilds.FolderName, $".upload-{name}");
        string files = Path.Combine(work, "files");
        Directory.CreateDirectory(files);

        try
        {
            string zip = Path.Combine(work, "upload.zip");

            if (!await SaveAsync(body, zip, cancellationToken).ConfigureAwait(false))
            {
                return ServerMessages.BootImageUploadTooLarge.With("max", MaxBytes / (1024 * 1024));
            }

            if (Unpack(zip, files) is { } refusal)
            {
                return refusal;
            }

            if (!IsWim(Path.Combine(files, "Boot", "boot.wim")) || BootImageCatalog.ReadBuildIn(files) is null)
            {
                return ServerMessages.BootImageUploadBroken.With();
            }

            string? served = catalog.Current;
            Directory.Move(files, BootBuilds.FolderOf(catalog.BootDirectory, name));
            catalog.Use(name);
            catalog.Prune(served);

            return null;
        }
        catch (InvalidDataException)
        {
            return ServerMessages.BootImageUploadBroken.With();
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    // Returns false for a body beyond MaxBytes.
    private static async Task<bool> SaveAsync(Stream body, string path, CancellationToken cancellationToken)
    {
        FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);

        await using (file.ConfigureAwait(false))
        {
            byte[] buffer = new byte[81920];
            int read;

            while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (file.Length + read > MaxBytes)
                {
                    return false;
                }

                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }

        return true;
    }

    // Returns null when every file landed below the folder, or what is wrong with the zip.
    private static ServerMessage? Unpack(string zip, string folder)
    {
        using ZipArchive archive = ZipFile.OpenRead(zip);

        // Windows PowerShell 5.1 on an old .NET Framework writes backslashes
        List<(ZipArchiveEntry Entry, string Name)> entries =
            [.. archive.Entries.Select(entry => (entry, entry.FullName.Replace('\\', '/'))).Where(entry => !entry.Item2.EndsWith('/'))];

        if (entries.Find(entry => !Belongs(entry.Name)) is { Entry: not null } stray)
        {
            return ServerMessages.BootImageUploadStrayFile.With("file", stray.Name);
        }

        if (Array.Find(s_required, required => !entries.Exists(entry => string.Equals(entry.Name, required, StringComparison.OrdinalIgnoreCase))) is { } missing)
        {
            return ServerMessages.BootImageUploadMissingFile.With("file", missing);
        }

        if (entries.Count > MaxFiles || entries.Sum(entry => entry.Entry.Length) > MaxUnpackedBytes)
        {
            return ServerMessages.BootImageUploadTooLarge.With("max", MaxBytes / (1024 * 1024));
        }

        string root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;

        foreach ((ZipArchiveEntry entry, string name) in entries)
        {
            string target = Path.GetFullPath(Path.Combine(folder, name));

            if (!target.StartsWith(root, StringComparison.Ordinal))
            {
                return ServerMessages.BootImageUploadStrayFile.With("file", name);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? folder);
            entry.ExtractToFile(target);
        }

        return null;
    }

    // In one of the three folders a build has, and nowhere else by way of its name.
    private static bool Belongs(string name) =>
        Array.Exists(s_folders, folder => name.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
        && !name.Contains(':', StringComparison.Ordinal)
        && !name.Split('/').Contains("..");

    private static bool IsWim(string path)
    {
        // On Linux a name in another case than Boot/boot.wim is another file
        if (!File.Exists(path))
        {
            return false;
        }

        using FileStream file = File.OpenRead(path);
        byte[] start = new byte[s_wim.Length];

        return file.ReadAtLeast(start, start.Length, throwOnEndOfStream: false) == start.Length && start.AsSpan().SequenceEqual(s_wim);
    }
}
