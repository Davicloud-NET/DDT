// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Images;

// Applies administrators' changes to the images in the library.
internal sealed class ImageLibrary(DdtDbContext database, ImageStore store, LiveNotifier live, TimeProvider timeProvider)
{
    // Runs under the library lock. That way an upload of the same file can't add rows for the stored file while it's
    // being deleted.
    public async Task<LibraryDeletion> DeleteAsync(Guid id, Actor actor, CancellationToken cancellationToken)
    {
        await using (await store.LibraryLock.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            Image? image = await database.Images.FirstOrDefaultAsync(i => i.Id == id, cancellationToken).ConfigureAwait(false);

            if (image is null)
            {
                return LibraryDeletion.NotFound;
            }

            bool inUse = await ActiveArtifacts.Of(database)
                .AnyAsync(a => a.Kind == ArtifactKind.Image && a.SourceId == id, cancellationToken)
                .ConfigureAwait(false);

            if (inUse)
            {
                return LibraryDeletion.InUse;
            }

            database.Images.Remove(image);
            database.AuditEvents.Add(AuditEvents.Create(
                AuditActions.ImageDeleted,
                image.Id.ToString("D"),
                actor,
                timeProvider.GetUtcNow(),
                image.Kind == ImageKind.RawDisk
                    ? $"{image.Name}, a raw disk image, SHA-256 {image.Sha256}."
                    : $"{image.Name}, index {image.WimIndex}, SHA-256 {image.Sha256}."));

            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // The row is gone, so delete the stored file too, unless another index of the same file still uses it.
            await store.DeleteObjectIfUnreferencedAsync(database, image.Sha256, CancellationToken.None).ConfigureAwait(false);
        }

        live.ImagesRemoved([id]);

        return LibraryDeletion.Deleted;
    }
}
