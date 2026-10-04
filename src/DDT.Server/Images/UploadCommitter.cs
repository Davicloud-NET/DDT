// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;
using DDT.Contracts.Messages;
using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Images;

// Checks the part file of an upload whose bytes have all arrived. Then it hands the upload to the committer for its
// kind. Call with the upload's lock held.
public sealed class UploadCommitter(
    ImageStore store,
    UploadRefusals refusals,
    WimUploadCommitter wims,
    RawUploadCommitter raws,
    PackageUploadCommitter packages,
    TimeProvider timeProvider)
{
    public async Task<UploadCompletion> CommitAsync(DdtDbContext database, Guid uploadId, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        ImageUpload? upload = await database.ImageUploads
            .FirstOrDefaultAsync(u => u.Id == uploadId, cancellationToken)
            .ConfigureAwait(false);

        if (upload is null)
        {
            return refusals.Missing(uploadId);
        }

        // The run that held the lock just before this one already finished it.
        if (upload.CompletedSha256 is { } completed)
        {
            return await LibraryEntries.ExistingAsync(database, upload, completed, cancellationToken).ConfigureAwait(false);
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
            return await packages.CommitAsync(database, upload, actor, cancellationToken).ConfigureAwait(false);
        }

        // A Windows ISO is taken for the image in it
        if (await IsoImages.IsIsoAsync(part, cancellationToken).ConfigureAwait(false)
            && !await UnpackIsoAsync(database, upload, cancellationToken).ConfigureAwait(false))
        {
            return await refusals.RefuseAsync(database, upload, ServerMessages.IsoNoWindowsImage.With(), cancellationToken).ConfigureAwait(false);
        }

        return await LibraryFiles.IsWimAsync(part, cancellationToken).ConfigureAwait(false)
            ? await wims.CommitAsync(database, upload, actor, cancellationToken).ConfigureAwait(false)
            : await raws.CommitAsync(database, upload, actor, cancellationToken).ConfigureAwait(false);
    }

    // Puts the ISO's install image in place of the ISO, so the upload goes on as that WIM. Returns false for an ISO
    // without one. The copy is written next to the part file, where the sweeper finds what a stop left.
    private async Task<bool> UnpackIsoAsync(DdtDbContext database, ImageUpload upload, CancellationToken cancellationToken)
    {
        string part = store.PartPath(upload.Id);
        string image = store.RawPath(upload.Id);

        try
        {
            if (!await IsoImages.ExtractInstallImageAsync(part, image, null, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            File.Move(image, part, overwrite: true);
            upload.Length = new FileInfo(part).Length;
            upload.Offset = upload.Length;
            upload.UpdatedUtc = timeProvider.GetUtcNow();
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return true;
        }
        finally
        {
            File.Delete(image);
        }
    }
}
