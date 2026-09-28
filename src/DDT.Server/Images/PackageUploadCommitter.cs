// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Packages;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Packages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Images;

// Adds an uploaded zip to the library as a package. It logs under ImageUploadCompleter's category, which the logging
// settings may name.
public sealed partial class PackageUploadCommitter(
    ImageStore store,
    UploadRefusals refusals,
    LiveNotifier live,
    TimeProvider timeProvider,
    ILogger<ImageUploadCompleter> logger)
{
    // The names and sizes are checked before the file is hashed, so a refused zip costs no hashing.
    public async Task<UploadCompletion> CommitAsync(DdtDbContext database, ImageUpload upload, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(upload);

        PackageKind kind = LibraryEntries.PackageKindOf(upload.Kind);
        (PackageInspection inspection, string sha256) = await InspectAsync(upload, kind, cancellationToken).ConfigureAwait(false);

        if (inspection.RefusalMessage is { } refusal)
        {
            return await refusals.RefuseAsync(database, upload, refusal, cancellationToken).ConfigureAwait(false);
        }

        await using SemaphoreHold hold = await store.LibraryLock.EnterAsync(cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = timeProvider.GetUtcNow();
        Package? existing = await database.Packages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Sha256 == sha256 && p.Kind == kind, CancellationToken.None)
            .ConfigureAwait(false);
        Package package = existing ?? NewPackage(upload, sha256, inspection, now, actor);

        if (existing is null)
        {
            database.Packages.Add(package);
        }

        // An upload that adds nothing is still recorded, under the package its file already is.
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.PackageUploaded,
            package.Id.ToString("D"),
            actor,
            now,
            existing is null
                ? $"{package.Name}, {kind}, {inspection.FileCount} files from {upload.FileName}, SHA-256 {sha256}."
                : $"{upload.FileName} matches {existing.Name} with SHA-256 {sha256}, so no package was added."));

        upload.CompletedSha256 = sha256;
        upload.UpdatedUtc = now;
        await LibraryFiles.SaveWithFileAsync(store, database, store.PartPath(upload.Id), sha256).ConfigureAwait(false);

        if (existing is not null)
        {
            return new UploadCompletion(UploadCompletionStatus.Existing, [], Package: PackageSummaries.From(existing));
        }

        PackageSummary added = PackageSummaries.From(package);
        live.PackageChanged(added);
        LogPackageAdded(kind, package.Id, upload.Id, upload.FileName, sha256);

        return new UploadCompletion(UploadCompletionStatus.Added, [], Package: added);
    }

    // Sha256 is empty for a refused zip.
    private async Task<(PackageInspection Inspection, string Sha256)> InspectAsync(ImageUpload upload, PackageKind kind, CancellationToken cancellationToken)
    {
        PackageInspection inspection;
        string sha256 = "";

        // Buffered and synchronous, because the zip's directory is read a few bytes at a time.
        await using (FileStream stream = new(
            store.PartPath(upload.Id),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.None))
        {
            inspection = PackageArchiveCheck.Inspect(stream, kind, cancellationToken);

            if (inspection.Refusal is null)
            {
                stream.Position = 0;
                sha256 = await LibraryFiles.HashAsync(stream, cancellationToken).ConfigureAwait(false);
            }
        }

        return (inspection, sha256);
    }

    private static Package NewPackage(ImageUpload upload, string sha256, PackageInspection inspection, DateTimeOffset now, Actor actor) => new()
    {
        Id = Guid.CreateVersion7(now),
        Name = LibraryEntries.Bounded(
            Path.GetFileNameWithoutExtension(upload.FileName) is { Length: > 0 } name ? name : upload.FileName,
            PackageLimits.MaxNameLength),
        Kind = LibraryEntries.PackageKindOf(upload.Kind),
        Sha256 = sha256,
        SizeBytes = upload.Length,
        ExpandedBytes = inspection.ExpandedBytes,
        FileCount = inspection.FileCount,
        OriginalFileName = upload.FileName,
        UploadedUtc = now,
        UploadedByUserId = actor.UserId,
        UploadedByName = LibraryEntries.Bounded(actor.Name, 256),
    };

    [LoggerMessage(
        EventId = 912,
        Level = LogLevel.Information,
        Message = "Added the {Kind} package {PackageId} from upload {UploadId} ({FileName}), SHA-256 {Sha256}")]
    private partial void LogPackageAdded(PackageKind kind, Guid packageId, Guid uploadId, string fileName, string sha256);
}
