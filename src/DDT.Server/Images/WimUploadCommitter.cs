// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;
using DDT.Contracts.Messages;
using DDT.Core.Wim;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Images;

// Adds each deployable index of an uploaded WIM to the library. It logs under ImageUploadCompleter's category, which the
// logging settings may name.
public sealed partial class WimUploadCommitter(
    ImageStore store,
    UploadRefusals refusals,
    LiveNotifier live,
    TimeProvider timeProvider,
    ILogger<ImageUploadCompleter> logger)
{
    public async Task<UploadCompletion> CommitAsync(DdtDbContext database, ImageUpload upload, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(upload);

        WimPart part = await ReadPartAsync(store.PartPath(upload.Id), cancellationToken).ConfigureAwait(false);

        if (part.Refusal is not null)
        {
            return await refusals.RefuseAsync(database, upload, part.Refusal, cancellationToken).ConfigureAwait(false);
        }

        return await AddAsync(database, upload, part, actor, cancellationToken).ConfigureAwait(false);
    }

    // The image list is read before the file is hashed. It takes milliseconds, so a refused file is answered before
    // a proxy gives up on the request, and no gigabytes are hashed for it.
    private static async Task<WimPart> ReadPartAsync(string part, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            part,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 0,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        IReadOnlyList<WimImageInfo> images;

        try
        {
            images = await WimMetadata.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidWimException exception)
        {
            return new WimPart("", [], exception.Reason ?? ServerMessages.WimIncomplete.With());
        }

        List<WimImageInfo> deployable = [.. images.Where(i => i.Architecture == DeploymentPolicy.DeployableArchitecture)];

        if (deployable.Count == 0)
        {
            return new WimPart("", [], ServerMessages.WimNoX64Image.With());
        }

        stream.Position = 0;

        return new WimPart(await LibraryFiles.HashAsync(stream, cancellationToken).ConfigureAwait(false), deployable, null);
    }

    // Under the library lock, so an identical upload completing at the same time and an image being deleted see
    // the stored file and its rows change together.
    private async Task<UploadCompletion> AddAsync(
        DdtDbContext database,
        ImageUpload upload,
        WimPart part,
        Actor actor,
        CancellationToken cancellationToken)
    {
        await using SemaphoreHold hold = await store.LibraryLock.EnterAsync(cancellationToken).ConfigureAwait(false);

        string sha256 = part.Sha256;
        var listed = await database.Images
            .Where(i => i.Sha256 == sha256)
            .Select(i => new { i.Id, i.WimIndex })
            .ToListAsync(CancellationToken.None)
            .ConfigureAwait(false);
        HashSet<int> listedIndexes = [.. listed.Select(i => i.WimIndex)];

        DateTimeOffset now = timeProvider.GetUtcNow();
        List<Image> added = [.. part.Deployable
            .Where(info => !listedIndexes.Contains(info.Index))
            .Select(info => NewImage(info, upload, sha256, now, actor))];

        // An upload that adds nothing is still recorded, under the entry its file already has.
        List<(Guid ImageId, string Detail)> audited = added.Count > 0
            ? [.. added.Select(image => (image.Id, $"{image.Name}, index {image.WimIndex} of {upload.FileName}, SHA-256 {sha256}."))]
            : [(listed.OrderBy(i => i.WimIndex).First().Id, $"{upload.FileName} matches the stored file with SHA-256 {sha256}, so no entries were added.")];

        database.Images.AddRange(added);
        database.AuditEvents.AddRange(audited.Select(entry => AuditEvents.Create(
            AuditActions.ImageUploaded,
            entry.ImageId.ToString("D"),
            actor,
            now,
            entry.Detail)));

        upload.CompletedSha256 = sha256;
        upload.UpdatedUtc = now;
        await LibraryFiles.SaveWithFileAsync(store, database, store.PartPath(upload.Id), sha256).ConfigureAwait(false);

        if (added.Count == 0)
        {
            return new UploadCompletion(
                UploadCompletionStatus.Existing,
                await LibraryEntries.ImagesOfAsync(database, sha256, CancellationToken.None).ConfigureAwait(false));
        }

        List<ImageSummary> summaries = [.. added.Select(ImageSummaries.From)];

        foreach (ImageSummary summary in summaries)
        {
            live.ImageChanged(summary);
        }

        LogImagesAdded(added.Count, upload.Id, upload.FileName, sha256);

        return new UploadCompletion(UploadCompletionStatus.Added, summaries);
    }

    private static Image NewImage(WimImageInfo info, ImageUpload upload, string sha256, DateTimeOffset now, Actor actor) => new()
    {
        Id = Guid.CreateVersion7(now),
        Name = LibraryEntries.Bounded(info.Name.Length > 0 ? info.Name : $"{upload.FileName}, index {info.Index}", 256),
        Kind = ImageKind.Wim,
        Sha256 = sha256,
        SizeBytes = upload.Length,
        WimIndex = info.Index,
        Edition = LibraryEntries.Bounded(info.EditionId, 64),
        Architecture = info.Architecture,
        Version = LibraryEntries.Bounded(info.Version, 32),
        Language = LibraryEntries.Bounded(info.DefaultLanguage, 16),
        InstalledBytes = Math.Max(0, info.InstalledBytes),
        OriginalFileName = upload.FileName,
        UploadedUtc = now,
        UploadedByUserId = actor.UserId,
        UploadedByName = LibraryEntries.Bounded(actor.Name, 256),
    };

    [LoggerMessage(EventId = 901, Level = LogLevel.Information, Message = "Added {Count} images from upload {UploadId} ({FileName}), SHA-256 {Sha256}")]
    private partial void LogImagesAdded(int count, Guid uploadId, string fileName, string sha256);

    // Sha256 and Deployable are empty when Refusal says why the file cannot be used.
    private sealed record WimPart(string Sha256, List<WimImageInfo> Deployable, ServerMessage? Refusal);
}
