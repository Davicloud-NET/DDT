// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Contracts.Packages;
using DDT.Server.Applications;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Packages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Images;

// Adds an uploaded zip to the library as a package, or as an application's files, which only applications show. It logs
// under ImageUploadCompleter's category, because the logging settings may name that category.
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
        bool isApplication = kind == PackageKind.Application;
        string source = isApplication
            ? await ApplicationUploads.SourceAsync(store, upload, cancellationToken).ConfigureAwait(false)
            : store.PartPath(upload.Id);
        (PackageInspection inspection, string sha256) = await InspectAsync(source, kind, cancellationToken).ConfigureAwait(false);

        if (inspection.RefusalMessage is { } refusal)
        {
            return await refusals.RefuseAsync(database, upload, refusal, cancellationToken).ConfigureAwait(false);
        }

        ApplicationSuggestion? suggestion = isApplication
            ? await ApplicationUploads.SuggestAsync(store, upload, cancellationToken).ConfigureAwait(false)
            : null;

        await using SemaphoreHold hold = await store.LibraryLock.EnterAsync(cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = timeProvider.GetUtcNow();
        Package? existing = await database.Packages
            .FirstOrDefaultAsync(p => p.Sha256 == sha256 && p.Kind == kind, CancellationToken.None)
            .ConfigureAwait(false);
        Package package = existing ?? NewPackage(upload, sha256, inspection, now, actor);

        if (existing is null)
        {
            package.SizeBytes = new FileInfo(source).Length;
            database.Packages.Add(package);
        }
        else if (isApplication)
        {
            // Files no application names are swept a day after their upload. Uploading them again starts that day anew.
            existing.UploadedUtc = now;
        }

        // An upload that adds nothing is still recorded, under the package that already has its file.
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
        await LibraryFiles.SaveWithFileAsync(store, database, source, sha256).ConfigureAwait(false);
        File.Delete(store.PartPath(upload.Id));

        return Completed(package, existing is null, upload, suggestion);
    }

    // An application's files are never pushed: the Packages page does not show them.
    private UploadCompletion Completed(Package package, bool added, ImageUpload upload, ApplicationSuggestion? suggestion)
    {
        UploadCompletionStatus status = added ? UploadCompletionStatus.Added : UploadCompletionStatus.Existing;

        if (added)
        {
            LogPackageAdded(package.Kind, package.Id, upload.Id, upload.FileName, package.Sha256);
        }

        if (package.Kind == PackageKind.Application)
        {
            return new UploadCompletion(status, [], Application: new ApplicationUpload(ApplicationUploads.Files(package), suggestion));
        }

        PackageSummary summary = PackageSummaries.From(package);

        if (added)
        {
            live.PackageChanged(summary);
        }

        return new UploadCompletion(status, [], Package: summary);
    }

    // Sha256 is empty for a refused zip.
    private static async Task<(PackageInspection Inspection, string Sha256)> InspectAsync(string source, PackageKind kind, CancellationToken cancellationToken)
    {
        PackageInspection inspection;
        string sha256 = "";

        // Buffered and synchronous, because the zip's directory is read a few bytes at a time.
        await using (FileStream stream = new(
            source,
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
