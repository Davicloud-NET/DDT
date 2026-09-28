// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;
using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Images;

// Hands an upload whose bytes all arrived to the committer of its kind, once its part file is checked. Call with the
// upload's lock held.
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

        // Finished by the run that held the lock just before this one.
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

        return await LibraryFiles.IsWimAsync(part, cancellationToken).ConfigureAwait(false)
            ? await wims.CommitAsync(database, upload, actor, cancellationToken).ConfigureAwait(false)
            : await raws.CommitAsync(database, upload, actor, cancellationToken).ConfigureAwait(false);
    }
}
