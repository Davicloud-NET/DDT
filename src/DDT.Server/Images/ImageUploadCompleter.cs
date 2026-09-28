// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Images;

// Hashing a WIM of several gigabytes can outlast a proxy's read timeout, so the work runs on the stopping token, not
// the request's. A retry is told the session is busy until the work stored its result, and is then answered from it.
public sealed partial class ImageUploadCompleter(
    IServiceScopeFactory scopes,
    ImageUploadLocks locks,
    UploadRefusals refusals,
    UploadCommitter committer,
    IHostApplicationLifetime lifetime,
    ILogger<ImageUploadCompleter> logger)
{
    public async Task<UploadCompletion> CompleteAsync(
        Guid uploadId,
        Actor actor,
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
                return refusals.Missing(uploadId);
            }

            if (upload.CompletedSha256 is { } sha256)
            {
                return await LibraryEntries.ExistingAsync(database, upload, sha256, cancellationToken).ConfigureAwait(false);
            }
        }

        if (!locks.TryEnter(uploadId, out ImageUploadLock? held))
        {
            return new UploadCompletion(UploadCompletionStatus.Busy, []);
        }

        // No token for Task.Run itself: a cancelled one would skip the delegate and never release the lock.
        Task<UploadCompletion> work = Task.Run(
            () => RunAsync(held, actor, lifetime.ApplicationStopping),
            CancellationToken.None);

        return await work.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<UploadCompletion> RunAsync(
        ImageUploadLock held,
        Actor actor,
        CancellationToken stoppingToken)
    {
        using (held)
        {
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();

                return await committer.CommitAsync(database, held.UploadId, actor, stoppingToken).ConfigureAwait(false);
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

    [LoggerMessage(EventId = 903, Level = LogLevel.Error, Message = "Could not complete upload {UploadId}")]
    private partial void LogCompletionFailed(Guid uploadId, Exception exception);
}
