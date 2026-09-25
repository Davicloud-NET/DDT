// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using DDT.Contracts.Images;
using DDT.Contracts.Packages;
using DDT.Core.Wim;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Packages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Images;

// Hashing a WIM of several gigabytes, or inflating a zip, can outlast a proxy's read timeout, so the work runs on
// the application's stopping token rather than the request's. A request that gives up leaves it running; its retry
// is told the session is busy until the work has stored its result, and is then answered from that result: the
// saved rows, or for a refused file, whose session is gone, the reason kept here.
public sealed partial class ImageUploadCompleter(
    IServiceScopeFactory scopes,
    ImageStore store,
    RawImageImporter importer,
    ImageUploadLocks locks,
    LiveNotifier live,
    TimeProvider timeProvider,
    IHostApplicationLifetime lifetime,
    ILogger<ImageUploadCompleter> logger)
{
    private const string NoX64Image = "This WIM holds no x64 Windows image.";
    private const int HashBufferBytes = 1024 * 1024;

    private readonly ConcurrentDictionary<Guid, (string Reason, DateTimeOffset RefusedUtc)> _refusals = new();

    public async Task<UploadCompletion> CompleteAsync(
        Guid uploadId,
        Guid? userId,
        string? userName,
        string? address,
        CancellationToken cancellationToken)
    {
        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
            ImageUpload? upload = await database.ImageUploads
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == uploadId, cancellationToken)
                .ConfigureAwait(false);

            if (upload is null)
            {
                return Missing(uploadId);
            }

            if (upload.CompletedSha256 is { } sha256)
            {
                return await ExistingAsync(database, upload, sha256, cancellationToken).ConfigureAwait(false);
            }
        }

        if (!locks.TryEnter(uploadId, out ImageUploadLock? held))
        {
            return new UploadCompletion(UploadCompletionStatus.Busy, []);
        }

        // No token for Task.Run itself: a cancelled one would skip the delegate and never release the lock.
        Task<UploadCompletion> work = Task.Run(
            () => RunAsync(held, userId, userName, address, lifetime.ApplicationStopping),
            CancellationToken.None);

        return await work.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<UploadCompletion> RunAsync(
        ImageUploadLock held,
        Guid? userId,
        string? userName,
        string? address,
        CancellationToken stoppingToken)
    {
        using (held)
        {
            try
            {
                return await CompleteUnderLockAsync(held.UploadId, userId, userName, address, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return new UploadCompletion(UploadCompletionStatus.Stopping, []);
            }
            catch (Exception exception)
            {
                // Nobody may be waiting for this any more, so the failure is logged here rather than left to a
                // request that has gone.
                LogCompletionFailed(held.UploadId, exception);

                return new UploadCompletion(UploadCompletionStatus.Failed, []);
            }
        }
    }

    private async Task<UploadCompletion> CompleteUnderLockAsync(
        Guid uploadId,
        Guid? userId,
        string? userName,
        string? address,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();

        ImageUpload? upload = await database.ImageUploads
            .FirstOrDefaultAsync(u => u.Id == uploadId, cancellationToken)
            .ConfigureAwait(false);

        if (upload is null)
        {
            return Missing(uploadId);
        }

        // Finished by the run that held the lock just before this one.
        if (upload.CompletedSha256 is { } completed)
        {
            return await ExistingAsync(database, upload, completed, cancellationToken).ConfigureAwait(false);
        }

        if (upload.Offset != upload.Length)
        {
            return new UploadCompletion(UploadCompletionStatus.Incomplete, [], upload.Offset);
        }

        string part = store.PartPath(uploadId);
        FileInfo partFile = new(part);

        if (!partFile.Exists || partFile.Length != upload.Length)
        {
            upload.Offset = 0;
            upload.UpdatedUtc = timeProvider.GetUtcNow();
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new UploadCompletion(UploadCompletionStatus.Incomplete, [], 0);
        }

        if (upload.Kind != UploadKind.Image)
        {
            return await CompletePackageAsync(database, upload, userId, userName, address, cancellationToken).ConfigureAwait(false);
        }

        if (!await IsWimAsync(part, cancellationToken).ConfigureAwait(false))
        {
            return await CompleteRawAsync(database, upload, userId, userName, address, cancellationToken).ConfigureAwait(false);
        }

        (string sha256, List<WimImageInfo> deployable, string? refusal) = await ReadPartAsync(part, cancellationToken).ConfigureAwait(false);

        if (refusal is not null)
        {
            return await RefuseAsync(database, upload, refusal, cancellationToken).ConfigureAwait(false);
        }

        return await CommitAsync(database, upload, sha256, deployable, userId, userName, address, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<UploadCompletion> ExistingAsync(
        DdtDbContext database,
        ImageUpload upload,
        string sha256,
        CancellationToken cancellationToken)
    {
        if (upload.Kind == UploadKind.Image)
        {
            return new UploadCompletion(
                UploadCompletionStatus.Existing,
                await ImagesOfAsync(database, sha256, cancellationToken).ConfigureAwait(false));
        }

        PackageKind kind = PackageKindOf(upload.Kind);
        Package? package = await database.Packages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Sha256 == sha256 && p.Kind == kind, cancellationToken)
            .ConfigureAwait(false);

        // Deleted since this upload added it.
        return package is null
            ? new UploadCompletion(UploadCompletionStatus.NotFound, [])
            : new UploadCompletion(UploadCompletionStatus.Existing, [], Package: PackageSummaries.From(package));
    }

    private async Task<UploadCompletion> RefuseAsync(DdtDbContext database, ImageUpload upload, string refusal, CancellationToken cancellationToken)
    {
        RememberRefusal(upload.Id, refusal);
        database.ImageUploads.Remove(upload);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (string file in store.UploadFiles(upload.Id))
        {
            File.Delete(file);
        }

        LogUploadRefused(upload.Id, upload.FileName, refusal);

        return new UploadCompletion(UploadCompletionStatus.Refused, [], Refusal: refusal);
    }

    // The names and sizes are checked before the file is hashed, so a refused zip costs no hashing.
    private async Task<UploadCompletion> CompletePackageAsync(
        DdtDbContext database,
        ImageUpload upload,
        Guid? userId,
        string? userName,
        string? address,
        CancellationToken cancellationToken)
    {
        PackageKind kind = PackageKindOf(upload.Kind);
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
                sha256 = await HashAsync(stream, cancellationToken).ConfigureAwait(false);
            }
        }

        if (inspection.Refusal is { } refusal)
        {
            return await RefuseAsync(database, upload, refusal, cancellationToken).ConfigureAwait(false);
        }

        await store.LibraryLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            Package? existing = await database.Packages
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Sha256 == sha256 && p.Kind == kind, CancellationToken.None)
                .ConfigureAwait(false);
            Package package = existing ?? NewPackage(upload, kind, sha256, inspection, now, userId, userName);

            if (existing is null)
            {
                database.Packages.Add(package);
            }

            // An upload that adds nothing is still recorded, under the package its file already is.
            database.AuditEvents.Add(new AuditEvent
            {
                OccurredUtc = now,
                Action = AuditActions.PackageUploaded,
                ActorUserId = userId,
                ActorName = userName,
                SubjectId = package.Id.ToString("D"),
                SourceAddress = address,
                Detail = existing is null
                    ? $"{package.Name}, {kind}, {inspection.FileCount} files from {upload.FileName}, SHA-256 {sha256}."
                    : $"{upload.FileName} matches {existing.Name} with SHA-256 {sha256}, so no package was added.",
            });

            upload.CompletedSha256 = sha256;
            upload.UpdatedUtc = now;
            await SaveWithFileAsync(database, store.PartPath(upload.Id), sha256).ConfigureAwait(false);

            if (existing is not null)
            {
                return new UploadCompletion(UploadCompletionStatus.Existing, [], Package: PackageSummaries.From(existing));
            }

            live.PackagesChanged();
            LogPackageAdded(kind, package.Id, upload.Id, upload.FileName, sha256);

            return new UploadCompletion(UploadCompletionStatus.Added, [], Package: PackageSummaries.From(package));
        }
        finally
        {
            store.LibraryLock.Release();
        }
    }

    // A WIM starts with its magic; any other image upload is a disk image, or refused as neither.
    private static async Task<bool> IsWimAsync(string part, CancellationToken cancellationToken)
    {
        byte[] magic = new byte[8];

        await using FileStream file = new(part, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 0, useAsync: true);

        return await file.ReadAtLeastAsync(magic, magic.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false) == magic.Length
            && magic.AsSpan().SequenceEqual((ReadOnlySpan<byte>)[0x4D, 0x53, 0x57, 0x49, 0x4D, 0x00, 0x00, 0x00]);
    }

    // The conversion runs before the library lock is taken: it takes minutes for a large image, and nothing it makes is
    // in the library yet. The same disk uploaded again, in whatever format, adds nothing.
    private async Task<UploadCompletion> CompleteRawAsync(
        DdtDbContext database,
        ImageUpload upload,
        Guid? userId,
        string? userName,
        string? address,
        CancellationToken cancellationToken)
    {
        RawImport import = await importer.ImportAsync(upload.Id, cancellationToken).ConfigureAwait(false);

        if (import.Refusal is { } refusal)
        {
            return await RefuseAsync(database, upload, refusal, cancellationToken).ConfigureAwait(false);
        }

        BootAssessment boot = BootCapabilities.Assess(import.Info!, UefiCertificateAuthorities.Microsoft);
        await store.LibraryLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            Image? same = await database.Images
                .AsNoTracking()
                .Where(i => i.SourceSha256 == import.SourceSha256)
                .OrderBy(i => i.Id)
                .FirstOrDefaultAsync(CancellationToken.None)
                .ConfigureAwait(false);

            if (same is not null)
            {
                database.AuditEvents.Add(RawAudit(same.Id, now, userId, userName, address,
                    $"{upload.FileName} holds the disk of {same.Name}, SHA-256 {import.SourceSha256}, so no entry was added."));
                upload.CompletedSha256 = same.Sha256;
                upload.UpdatedUtc = now;
                await database.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                File.Delete(import.CompressedPath);
                File.Delete(store.PartPath(upload.Id));

                return new UploadCompletion(
                    UploadCompletionStatus.Existing,
                    await ImagesOfAsync(database, same.Sha256, CancellationToken.None).ConfigureAwait(false));
            }

            Image image = NewRawImage(upload, import, boot, now, userId, userName);
            database.Images.Add(image);
            database.AuditEvents.Add(RawAudit(image.Id, now, userId, userName, address,
                $"{image.Name}, a raw disk image of {import.Info!.SizeBytes} bytes from {upload.FileName}, {boot.Capability}, " +
                $"SHA-256 {import.Sha256} compressed and {import.SourceSha256} as a disk."));
            upload.CompletedSha256 = import.Sha256;
            upload.UpdatedUtc = now;
            await SaveWithFileAsync(database, import.CompressedPath, import.Sha256).ConfigureAwait(false);
            File.Delete(store.PartPath(upload.Id));

            live.ImagesChanged();
            LogRawImageAdded(image.Id, upload.Id, upload.FileName, boot.Capability, import.Sha256);

            return new UploadCompletion(UploadCompletionStatus.Added, [ImageSummaries.From(image)]);
        }
        finally
        {
            store.LibraryLock.Release();
        }
    }

    private static AuditEvent RawAudit(Guid imageId, DateTimeOffset now, Guid? userId, string? userName, string? address, string detail) => new()
    {
        OccurredUtc = now,
        Action = AuditActions.ImageUploaded,
        ActorUserId = userId,
        ActorName = userName,
        SubjectId = imageId.ToString("D"),
        SourceAddress = address,
        Detail = StoredText.Bound(detail, AuditEvent.MaxDetailLength),
    };

    // Named after the file without the extensions of its formats, such as noble-server-cloudimg-amd64 for
    // noble-server-cloudimg-amd64.img.zst.
    private static Image NewRawImage(ImageUpload upload, RawImport import, BootAssessment boot, DateTimeOffset now, Guid? userId, string? userName)
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
            Name = Bounded(name, 256),
            Kind = ImageKind.RawDisk,
            Sha256 = import.Sha256,
            SizeBytes = import.SizeBytes,
            WimIndex = 0,
            Architecture = boot.Architecture,
            InstalledBytes = import.Info!.MinimumDiskBytes,
            OriginalFileName = upload.FileName,
            UploadedUtc = now,
            UploadedByUserId = userId,
            UploadedByName = Bounded(userName, 256),
            BootCapability = boot.Capability,
            BootDetail = Bounded(boot.Detail, RawImageLimits.MaxBootDetailLength),
            SourceSha256 = import.SourceSha256,
        };
    }

    private static PackageKind PackageKindOf(UploadKind kind) => kind == UploadKind.Drivers ? PackageKind.Drivers : PackageKind.Files;

    private static Package NewPackage(
        ImageUpload upload,
        PackageKind kind,
        string sha256,
        PackageInspection inspection,
        DateTimeOffset now,
        Guid? userId,
        string? userName) => new()
        {
            Id = Guid.CreateVersion7(now),
            Name = Bounded(
                Path.GetFileNameWithoutExtension(upload.FileName) is { Length: > 0 } name ? name : upload.FileName,
                PackageLimits.MaxNameLength),
            Kind = kind,
            Sha256 = sha256,
            SizeBytes = upload.Length,
            ExpandedBytes = inspection.ExpandedBytes,
            FileCount = inspection.FileCount,
            OriginalFileName = upload.FileName,
            UploadedUtc = now,
            UploadedByUserId = userId,
            UploadedByName = Bounded(userName, 256),
        };

    // The image list is read before the file is hashed. It takes milliseconds, so a refused file is answered before
    // a proxy gives up on the request, and no gigabytes are hashed for it.
    private static async Task<(string Sha256, List<WimImageInfo> Deployable, string? Refusal)> ReadPartAsync(
        string part,
        CancellationToken cancellationToken)
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
            return ("", [], exception.Message);
        }

        List<WimImageInfo> deployable = [.. images.Where(i => i.Architecture == DeploymentService.DeployableArchitecture)];

        if (deployable.Count == 0)
        {
            return ("", [], NoX64Image);
        }

        stream.Position = 0;

        return (await HashAsync(stream, cancellationToken).ConfigureAwait(false), deployable, null);
    }

    // Recorded before the session is removed, so a retry that no longer finds the session finds the reason. Kept as
    // long as the session could have lived.
    private void RememberRefusal(Guid uploadId, string reason)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        foreach (KeyValuePair<Guid, (string Reason, DateTimeOffset RefusedUtc)> refusal in _refusals)
        {
            if (now - refusal.Value.RefusedUtc >= ImageUploadLimits.SessionLifetime)
            {
                _refusals.TryRemove(refusal);
            }
        }

        _refusals[uploadId] = (reason, now);
    }

    private UploadCompletion Missing(Guid uploadId) =>
        _refusals.TryGetValue(uploadId, out (string Reason, DateTimeOffset RefusedUtc) refusal)
            ? new UploadCompletion(UploadCompletionStatus.Refused, [], Refusal: refusal.Reason)
            : new UploadCompletion(UploadCompletionStatus.NotFound, []);

    private static async Task<string> HashAsync(FileStream stream, CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(HashBufferBytes);

        try
        {
            int count;

            while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, count);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    // Under the library lock, so an identical upload completing at the same time and an image being deleted see
    // the stored file and its rows change together.
    private async Task<UploadCompletion> CommitAsync(
        DdtDbContext database,
        ImageUpload upload,
        string sha256,
        List<WimImageInfo> deployable,
        Guid? userId,
        string? userName,
        string? address,
        CancellationToken cancellationToken)
    {
        await store.LibraryLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var listed = await database.Images
                .Where(i => i.Sha256 == sha256)
                .Select(i => new { i.Id, i.WimIndex })
                .ToListAsync(CancellationToken.None)
                .ConfigureAwait(false);
            HashSet<int> listedIndexes = [.. listed.Select(i => i.WimIndex)];

            DateTimeOffset now = timeProvider.GetUtcNow();
            List<Image> added = [.. deployable
                .Where(info => !listedIndexes.Contains(info.Index))
                .Select(info => NewImage(info, upload, sha256, now, userId, userName))];

            // An upload that adds nothing is still recorded, under the entry its file already has.
            List<(Guid ImageId, string Detail)> audited = added.Count > 0
                ? [.. added.Select(image => (image.Id, $"{image.Name}, index {image.WimIndex} of {upload.FileName}, SHA-256 {sha256}."))]
                : [(listed.OrderBy(i => i.WimIndex).First().Id, $"{upload.FileName} matches the stored file with SHA-256 {sha256}, so no entries were added.")];

            database.Images.AddRange(added);
            database.AuditEvents.AddRange(audited.Select(entry => new AuditEvent
            {
                OccurredUtc = now,
                Action = AuditActions.ImageUploaded,
                ActorUserId = userId,
                ActorName = userName,
                SubjectId = entry.ImageId.ToString("D"),
                SourceAddress = address,
                Detail = entry.Detail,
            }));

            upload.CompletedSha256 = sha256;
            upload.UpdatedUtc = now;
            await SaveWithFileAsync(database, store.PartPath(upload.Id), sha256).ConfigureAwait(false);

            if (added.Count == 0)
            {
                return new UploadCompletion(
                    UploadCompletionStatus.Existing,
                    await ImagesOfAsync(database, sha256, CancellationToken.None).ConfigureAwait(false));
            }

            live.ImagesChanged();
            LogImagesAdded(added.Count, upload.Id, upload.FileName, sha256);

            return new UploadCompletion(UploadCompletionStatus.Added, [.. added.Select(ImageSummaries.From)]);
        }
        finally
        {
            store.LibraryLock.Release();
        }
    }

    // Puts source, the part file or the compressed copy of a disk image, into the library and saves the rows added for
    // it. The file itself decides, not its rows: a stored file whose rows were lost is used again, and one that went
    // missing under its rows is put back. Call with LibraryLock held.
    private async Task SaveWithFileAsync(DdtDbContext database, string source, string sha256)
    {
        string target = store.ObjectPath(sha256);
        bool moved = !File.Exists(target);

        if (moved)
        {
            Directory.CreateDirectory(store.ObjectsDirectory);
            File.Move(source, target, overwrite: false);
        }

        try
        {
            // The file is in the library by now, so its rows are saved even while the server stops.
            await database.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch when (moved)
        {
            // Completing again, as the failure's answer asks, starts from the part file.
            File.Move(target, source, overwrite: false);

            throw;
        }

        if (!moved)
        {
            File.Delete(source);
        }
    }

    private static Image NewImage(WimImageInfo info, ImageUpload upload, string sha256, DateTimeOffset now, Guid? userId, string? userName) => new()
    {
        Id = Guid.CreateVersion7(now),
        Name = Bounded(info.Name.Length > 0 ? info.Name : $"{upload.FileName}, index {info.Index}", 256),
        Kind = ImageKind.Wim,
        Sha256 = sha256,
        SizeBytes = upload.Length,
        WimIndex = info.Index,
        Edition = Bounded(info.EditionId, 64),
        Architecture = info.Architecture,
        Version = Bounded(info.Version, 32),
        Language = Bounded(info.DefaultLanguage, 16),
        InstalledBytes = Math.Max(0, info.InstalledBytes),
        OriginalFileName = upload.FileName,
        UploadedUtc = now,
        UploadedByUserId = userId,
        UploadedByName = Bounded(userName, 256),
    };

    // The WIM's XML is whatever its author wrote, and PostgreSQL refuses a value longer than its column.
    [return: NotNullIfNotNull(nameof(value))]
    private static string? Bounded(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    private static async Task<IReadOnlyList<ImageSummary>> ImagesOfAsync(DdtDbContext database, string sha256, CancellationToken cancellationToken)
    {
        List<Image> images = await database.Images
            .AsNoTracking()
            .Where(i => i.Sha256 == sha256)
            .OrderBy(i => i.WimIndex)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. images.Select(ImageSummaries.From)];
    }

    [LoggerMessage(EventId = 901, Level = LogLevel.Information, Message = "Added {Count} images from upload {UploadId} ({FileName}), SHA-256 {Sha256}")]
    private partial void LogImagesAdded(int count, Guid uploadId, string fileName, string sha256);

    [LoggerMessage(EventId = 902, Level = LogLevel.Information, Message = "Refused upload {UploadId} ({FileName}): {Reason}")]
    private partial void LogUploadRefused(Guid uploadId, string fileName, string reason);

    [LoggerMessage(EventId = 903, Level = LogLevel.Error, Message = "Could not complete upload {UploadId}")]
    private partial void LogCompletionFailed(Guid uploadId, Exception exception);

    [LoggerMessage(
        EventId = 917,
        Level = LogLevel.Information,
        Message = "Added the raw disk image {ImageId} from upload {UploadId} ({FileName}), {Capability}, SHA-256 {Sha256}")]
    private partial void LogRawImageAdded(Guid imageId, Guid uploadId, string fileName, ImageBootCapability capability, string sha256);

    [LoggerMessage(
        EventId = 912,
        Level = LogLevel.Information,
        Message = "Added the {Kind} package {PackageId} from upload {UploadId} ({FileName}), SHA-256 {Sha256}")]
    private partial void LogPackageAdded(PackageKind kind, Guid packageId, Guid uploadId, string fileName, string sha256);
}
