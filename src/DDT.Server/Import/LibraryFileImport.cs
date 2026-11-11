// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using DDT.Contracts.Images;
using DDT.Contracts.Messages;
using DDT.Server.Data;
using DDT.Server.Images;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Import;

// Copies one import item next to the uploads, then completes it as an upload whose bytes have all arrived, so the same
// checks decide. It logs under LibraryImports' category, which the logging settings may name.
public sealed partial class LibraryFileImport(
    CurrentImport state,
    ImageStore store,
    IServiceScopeFactory scopes,
    ImageUploadCompleter completer,
    ILogger<LibraryImports> logger)
{
    // The sweeper removes part files without a session, so a copy in progress has another name
    private const string CopyExtension = ".import";
    private const int CopyBufferBytes = 1024 * 1024;
    private static readonly char[] s_unsafe = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    // What an import left when the server stopped under it.
    public void DeleteStale()
    {
        Directory.CreateDirectory(store.UploadsDirectory);

        foreach (string stale in Directory.EnumerateFiles(store.UploadsDirectory, "*" + CopyExtension))
        {
            File.Delete(stale);
        }
    }

    // Added or Existing with what the completer gave, or Refused with the reason.
    public async Task<UploadCompletion> AddAsync(ImportItem item, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(actor);

        Guid id = Guid.CreateVersion7();
        string copy = Path.Combine(store.UploadsDirectory, $"{id:N}{CopyExtension}");

        try
        {
            if (await ReadAsync(item, copy, cancellationToken).ConfigureAwait(false) is { } refusal)
            {
                return Refused(refusal);
            }

            await AddSessionAsync(item, id, copy, actor, cancellationToken).ConfigureAwait(false);
            UploadCompletion completion = await completer.CompleteAsync(id, actor, cancellationToken).ConfigureAwait(false);

            switch (completion.Status)
            {
                case UploadCompletionStatus.Added or UploadCompletionStatus.Existing:
                    return completion;
                case UploadCompletionStatus.Refused:
                    return Refused(completion.Refusal ?? ServerMessages.ImportFailed.With());
                default:
                    // The session would wait for a browser that never sent it
                    await DiscardAsync(id, cancellationToken).ConfigureAwait(false);

                    return Refused(ServerMessages.ImportFailed.With());
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogUnreadable(exception);

            return Refused(ServerMessages.ImportUnreadable.With("reason", exception.Message));
        }
        finally
        {
            File.Delete(copy);
        }
    }

    private static UploadCompletion Refused(ServerMessage reason) => new(UploadCompletionStatus.Refused, [], Refusal: reason);

    // Returns null when the copy holds what the item names, or why it holds nothing.
    private async Task<ServerMessage?> ReadAsync(ImportItem item, string copy, CancellationToken cancellationToken)
    {
        if (item.File is null)
        {
            await ZipAsync(item, copy, cancellationToken).ConfigureAwait(false);

            return null;
        }

        if (await IsoImages.IsIsoAsync(item.File, cancellationToken).ConfigureAwait(false))
        {
            return await IsoImages.ExtractInstallImageAsync(item.File, copy, state.Progress, cancellationToken).ConfigureAwait(false)
                ? null
                : ServerMessages.IsoNoWindowsImage.With();
        }

        FileStream source = new(item.File, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        FileStream target = new(copy, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferBytes, FileOptions.Asynchronous);

        await using (source.ConfigureAwait(false))
        await using (target.ConfigureAwait(false))
        {
            byte[] buffer = new byte[CopyBufferBytes];
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                state.Progress(target.Length, source.Length);
            }
        }

        return null;
    }

    // One zip of the folders: a driver group's each under its own name, as a driver package upload is, and an
    // application's one folder at the root, where its command runs.
    private async Task ZipAsync(ImportItem item, string copy, CancellationToken cancellationToken)
    {
        bool atRoot = item.Kind == UploadKind.Application;
        List<(string Path, string Name)> files =
        [
            .. item.Folders.SelectMany((folder, index) => Directory
                .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(file => (file, (atRoot ? "" : $"{index:D3}-{Path.GetFileName(folder)}/") + Path.GetRelativePath(folder, file).Replace('\\', '/')))),
        ];
        long total = files.Sum(file => new FileInfo(file.Path).Length);
        long done = 0;

        using ZipArchive archive = ZipFile.Open(copy, ZipArchiveMode.Create);

        foreach ((string path, string name) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileStream source = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            Stream entry = archive.CreateEntry(name, CompressionLevel.Fastest).Open();

            await using (source.ConfigureAwait(false))
            await using (entry.ConfigureAwait(false))
            {
                await source.CopyToAsync(entry, cancellationToken).ConfigureAwait(false);
                done += source.Length;
                state.Progress(done, total);
            }
        }
    }

    // The copy becomes the part file of a session with every byte in, which the completer then checks and adds.
    private async Task AddSessionAsync(ImportItem item, Guid id, string copy, Actor actor, CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = scopes.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
            TimeProvider timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
            long length = new FileInfo(copy).Length;
            DateTimeOffset now = timeProvider.GetUtcNow();
            string zipName = string.Join('_', item.Name.Split(s_unsafe)) + ".zip";

            database.ImageUploads.Add(new ImageUpload
            {
                Id = id,
                FileName = LibraryEntries.Bounded(item.File is null ? zipName : Path.GetFileName(item.File), ImageUploadLimits.MaxFileNameLength),
                Length = length,
                Offset = length,
                Kind = item.Kind,
                CreatedByUserId = actor.UserId,
                CreatedUtc = now,
                UpdatedUtc = now,
            });
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            File.Move(copy, store.PartPath(id), overwrite: true);
        }
    }

    private async Task DiscardAsync(Guid id, CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = scopes.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
            await database.ImageUploads.Where(upload => upload.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (string file in store.UploadFiles(id))
        {
            File.Delete(file);
        }
    }

    [LoggerMessage(EventId = 921, Level = LogLevel.Warning, Message = "An import could not read its file or folder")]
    private partial void LogUnreadable(Exception exception);
}
