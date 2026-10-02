// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using DDT.Contracts.Images;
using DDT.Contracts.Import;
using DDT.Contracts.Messages;
using DDT.Contracts.Packages;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Packages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Import;

// Brings files from the server's own disks into the library, one import at a time. Each file is copied next to the
// uploads and then goes the way of an upload whose bytes have all arrived, so the same checks decide.
public sealed partial class LibraryImports(
    CurrentImport state,
    ImageStore store,
    IServiceScopeFactory scopes,
    ImageUploadCompleter completer,
    IHostApplicationLifetime lifetime,
    ILogger<LibraryImports> logger)
{
    // The sweeper removes part files without a session, so a copy in progress has another name
    private const string CopyExtension = ".import";
    private const int CopyBufferBytes = 1024 * 1024;

    // The import that was started last, for tests to wait on.
    public Task Last { get; private set; } = Task.CompletedTask;

    // Returns false while another import runs.
    public bool TryStart(IReadOnlyList<ImportItem> items, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(actor);

        if (!state.TryBegin(actor.Name ?? "unknown", items.Count))
        {
            return false;
        }

        Last = Task.Run(() => RunAsync(items, actor));

        return true;
    }

    private async Task RunAsync(IReadOnlyList<ImportItem> items, Actor actor)
    {
        try
        {
            Directory.CreateDirectory(store.UploadsDirectory);

            // What an import left when the server stopped under it
            foreach (string stale in Directory.EnumerateFiles(store.UploadsDirectory, "*" + CopyExtension))
            {
                File.Delete(stale);
            }

            foreach (ImportItem item in items)
            {
                state.Item(item.Name);
                state.Result(await ImportAsync(item, actor, lifetime.ApplicationStopping).ConfigureAwait(false));
            }
        }
        catch (Exception exception)
        {
            // Told on the page by what is missing from the results. An import must never take the host down.
            LogFailed(exception);
        }

        state.Finish();
    }

    private async Task<ImportResult> ImportAsync(ImportItem item, Actor actor, CancellationToken cancellationToken)
    {
        Guid id = Guid.CreateVersion7();
        string copy = Path.Combine(store.UploadsDirectory, $"{id:N}{CopyExtension}");

        try
        {
            if (await ReadAsync(item, copy, cancellationToken).ConfigureAwait(false) is { } refusal)
            {
                return Refused(item, refusal);
            }

            await AddSessionAsync(item, id, copy, actor, cancellationToken).ConfigureAwait(false);
            UploadCompletion completion = await completer.CompleteAsync(id, actor, cancellationToken).ConfigureAwait(false);

            switch (completion.Status)
            {
                case UploadCompletionStatus.Added or UploadCompletionStatus.Existing:
                    await DescribeAsync(item, completion, actor, cancellationToken).ConfigureAwait(false);

                    return new ImportResult(item.Name, completion.Status == UploadCompletionStatus.Added ? ImportOutcome.Added : ImportOutcome.Existing);

                case UploadCompletionStatus.Refused:
                    return Refused(item, completion.Refusal ?? ServerMessages.ImportFailed.With());

                default:
                    // The session would wait for a browser that never sent it
                    await DiscardAsync(id, cancellationToken).ConfigureAwait(false);

                    return Refused(item, ServerMessages.ImportFailed.With());
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogFailed(exception);

            return Refused(item, ServerMessages.ImportUnreadable.With("reason", exception.Message));
        }
        finally
        {
            File.Delete(copy);
        }
    }

    // Returns null when the copy holds what the item names, or why it holds nothing.
    private async Task<ServerMessage?> ReadAsync(ImportItem item, string copy, CancellationToken cancellationToken)
    {
        if (item.File is null)
        {
            await ZipAsync(item.Folders, copy, cancellationToken).ConfigureAwait(false);

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

    // One zip of the group's driver folders, each under its own name, as a driver package upload is.
    private async Task ZipAsync(IReadOnlyList<string> folders, string copy, CancellationToken cancellationToken)
    {
        List<(string Path, string Name)> files =
        [
            .. folders.SelectMany((folder, index) => Directory
                .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(file => (file, $"{index:D3}-{Path.GetFileName(folder)}/{Path.GetRelativePath(folder, file).Replace('\\', '/')}"))),
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

            database.ImageUploads.Add(new ImageUpload
            {
                Id = id,
                FileName = LibraryEntries.Bounded(item.File is null ? item.Name + ".zip" : Path.GetFileName(item.File), ImageUploadLimits.MaxFileNameLength),
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

    // A driver group's package takes the group's name and the model its path named.
    private async Task DescribeAsync(ImportItem item, UploadCompletion completion, Actor actor, CancellationToken cancellationToken)
    {
        if (completion is not { Status: UploadCompletionStatus.Added, Package: { } package })
        {
            return;
        }

        AsyncServiceScope scope = scopes.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            UpdatePackageRequest request = new(
                LibraryEntries.Bounded(item.Name, PackageLimits.MaxNameLength),
                item.Description,
                item.Target is null ? [] : [item.Target]);
            await scope.ServiceProvider.GetRequiredService<PackageLibrary>().UpdateAsync(package.Id, request, actor, cancellationToken).ConfigureAwait(false);
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

    private static ImportResult Refused(ImportItem item, ServerMessage reason) => new(item.Name, ImportOutcome.Refused, reason, reason.Text);

    [LoggerMessage(EventId = 920, Level = LogLevel.Warning, Message = "An import into the library failed")]
    private partial void LogFailed(Exception exception);
}
