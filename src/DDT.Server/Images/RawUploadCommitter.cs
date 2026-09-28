// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;
using DDT.Contracts.Messages;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Images;

// Adds an uploaded disk image to the library, compressed. It logs under ImageUploadCompleter's category, because the
// logging settings may name that category.
public sealed partial class RawUploadCommitter(
    RawImageImporter importer,
    ImageStore store,
    UploadRefusals refusals,
    LiveNotifier live,
    TimeProvider timeProvider,
    ILogger<ImageUploadCompleter> logger)
{
    // The conversion runs before taking the library lock. It takes minutes for a large image, and nothing it makes is
    // in the library yet. The same disk uploaded again, in any format, adds nothing.
    public async Task<UploadCompletion> CommitAsync(DdtDbContext database, ImageUpload upload, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(upload);

        RawImport import = await importer.ImportAsync(upload.Id, cancellationToken).ConfigureAwait(false);

        if (import.Refusal is { } refusal)
        {
            return await RefusedAsync(database, upload, import, refusal, cancellationToken).ConfigureAwait(false);
        }

        BootAssessment boot = BootCapabilities.Assess(import.Info!, UefiCertificateAuthorities.Microsoft);
        await using SemaphoreHold hold = await store.LibraryLock.EnterAsync(cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = timeProvider.GetUtcNow();
        Image? same = await database.Images
            .AsNoTracking()
            .Where(i => i.SourceSha256 == import.SourceSha256)
            .OrderBy(i => i.Id)
            .FirstOrDefaultAsync(CancellationToken.None)
            .ConfigureAwait(false);

        if (same is not null)
        {
            database.AuditEvents.Add(AuditEvents.Create(
                AuditActions.ImageUploaded,
                same.Id.ToString("D"),
                actor,
                now,
                $"{upload.FileName} holds the disk of {same.Name}, SHA-256 {import.SourceSha256}, so no entry was added."));

            return await AddAgainAsync(database, upload, import, same, now).ConfigureAwait(false);
        }

        Image image = NewRawImage(upload, import, boot, now, actor);
        database.Images.Add(image);
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.ImageUploaded,
            image.Id.ToString("D"),
            actor,
            now,
            $"{image.Name}, a raw disk image of {import.Info!.SizeBytes} bytes from {upload.FileName}, {boot.Capability}, " +
            $"SHA-256 {import.Sha256} compressed and {import.SourceSha256} as a disk."));
        upload.CompletedSha256 = import.Sha256;
        upload.UpdatedUtc = now;
        await LibraryFiles.SaveWithFileAsync(store, database, import.CompressedPath, import.Sha256).ConfigureAwait(false);
        File.Delete(store.PartPath(upload.Id));

        ImageSummary summary = ImageSummaries.From(image);
        live.ImageChanged(summary);
        LogRawImageAdded(image.Id, upload.Id, upload.FileName, boot.Capability, import.Sha256);

        return new UploadCompletion(UploadCompletionStatus.Added, [summary]);
    }

    // A refusal whose cause lies with the server keeps the upload for another attempt.
    private async Task<UploadCompletion> RefusedAsync(
        DdtDbContext database,
        ImageUpload upload,
        RawImport import,
        ServerMessage refusal,
        CancellationToken cancellationToken)
    {
        if (import.Retryable)
        {
            LogUploadKept(upload.Id, upload.FileName, refusal.Text);

            return new UploadCompletion(UploadCompletionStatus.Kept, [], Refusal: refusal);
        }

        return await refusals.RefuseAsync(database, upload, refusal, cancellationToken).ConfigureAwait(false);
    }

    // The disk is already in the library as same, so nothing is added. But the upload puts the file back if it went
    // missing.
    private async Task<UploadCompletion> AddAgainAsync(DdtDbContext database, ImageUpload upload, RawImport import, Image same, DateTimeOffset now)
    {
        upload.CompletedSha256 = same.Sha256;
        upload.UpdatedUtc = now;
        Image? stored = null;

        if (File.Exists(store.ObjectPath(same.Sha256)))
        {
            await database.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            File.Delete(import.CompressedPath);
        }
        else
        {
            // A copy that was compressed differently, for example by another version of DDT, becomes the image's file.
            if (import.Sha256 != same.Sha256)
            {
                stored = await database.Images.FirstAsync(i => i.Id == same.Id, CancellationToken.None).ConfigureAwait(false);
                stored.Sha256 = import.Sha256;
                stored.SizeBytes = import.SizeBytes;
                upload.CompletedSha256 = import.Sha256;
            }

            await LibraryFiles.SaveWithFileAsync(store, database, import.CompressedPath, upload.CompletedSha256).ConfigureAwait(false);
        }

        File.Delete(store.PartPath(upload.Id));

        // The image has a different file now, and the library shows that.
        if (stored is not null)
        {
            live.ImageChanged(ImageSummaries.From(stored));
        }

        return new UploadCompletion(
            UploadCompletionStatus.Existing,
            await LibraryEntries.ImagesOfAsync(database, upload.CompletedSha256, CancellationToken.None).ConfigureAwait(false));
    }

    // The name is the file name without its format extensions, such as noble-server-cloudimg-amd64 for
    // noble-server-cloudimg-amd64.img.zst.
    private static Image NewRawImage(ImageUpload upload, RawImport import, BootAssessment boot, DateTimeOffset now, Actor actor)
    {
        string name = upload.FileName;

        while (Path.GetExtension(name).ToLowerInvariant() is ".img" or ".raw" or ".qcow2" or ".qcow" or ".gz" or ".xz" or ".zst" or ".zstd"
            && name.Length > Path.GetExtension(name).Length)
        {
            name = Path.GetFileNameWithoutExtension(name);
        }

        return new Image
        {
            Id = Guid.CreateVersion7(now),
            Name = LibraryEntries.Bounded(name, 256),
            Kind = ImageKind.RawDisk,
            Sha256 = import.Sha256,
            SizeBytes = import.SizeBytes,
            WimIndex = 0,
            Architecture = boot.Architecture,
            InstalledBytes = import.Info!.MinimumDiskBytes,
            OriginalFileName = upload.FileName,
            UploadedUtc = now,
            UploadedByUserId = actor.UserId,
            UploadedByName = LibraryEntries.Bounded(actor.Name, 256),
            BootCapability = boot.Capability,
            SignedUnder = boot.SignedUnder,
            BootDetail = LibraryEntries.Bounded(boot.Detail, RawImageLimits.MaxBootDetailLength),
            SourceSha256 = import.SourceSha256,
        };
    }

    [LoggerMessage(EventId = 918, Level = LogLevel.Warning, Message = "Kept upload {UploadId} ({FileName}) for another attempt: {Reason}")]
    private partial void LogUploadKept(Guid uploadId, string fileName, string reason);

    [LoggerMessage(
        EventId = 917,
        Level = LogLevel.Information,
        Message = "Added the raw disk image {ImageId} from upload {UploadId} ({FileName}), {Capability}, SHA-256 {Sha256}")]
    private partial void LogRawImageAdded(Guid imageId, Guid uploadId, string fileName, ImageBootCapability capability, string sha256);
}
