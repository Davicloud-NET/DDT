// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Concurrent;
using DDT.Contracts.Messages;
using DDT.Server.Data;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Images;

// A refused upload loses its session, so the reason is kept here for a retry that can't find the session any more. It
// logs under ImageUploadCompleter's category, because the logging settings may name that category.
public sealed partial class UploadRefusals(ImageStore store, TimeProvider timeProvider, ILogger<ImageUploadCompleter> logger)
{
    private readonly ConcurrentDictionary<Guid, (ServerMessage Reason, DateTimeOffset RefusedUtc)> _refusals = new();

    public UploadCompletion Missing(Guid uploadId) =>
        _refusals.TryGetValue(uploadId, out (ServerMessage Reason, DateTimeOffset RefusedUtc) refusal)
            ? new UploadCompletion(UploadCompletionStatus.Refused, [], Refusal: refusal.Reason)
            : new UploadCompletion(UploadCompletionStatus.NotFound, []);

    public async Task<UploadCompletion> RefuseAsync(DdtDbContext database, ImageUpload upload, ServerMessage refusal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(refusal);

        Remember(upload.Id, refusal);
        database.ImageUploads.Remove(upload);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (string file in store.UploadFiles(upload.Id))
        {
            File.Delete(file);
        }

        LogUploadRefused(upload.Id, upload.FileName, refusal.Text);

        return new UploadCompletion(UploadCompletionStatus.Refused, [], Refusal: refusal);
    }

    // Record the reason before the session is removed, so a retry that can't find the session finds the reason instead.
    // It's kept as long as the session could have lived.
    private void Remember(Guid uploadId, ServerMessage reason)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        foreach (KeyValuePair<Guid, (ServerMessage Reason, DateTimeOffset RefusedUtc)> refusal in _refusals)
        {
            if (now - refusal.Value.RefusedUtc >= ImageUploadLimits.SessionLifetime)
            {
                _refusals.TryRemove(refusal);
            }
        }

        _refusals[uploadId] = (reason, now);
    }

    [LoggerMessage(EventId = 902, Level = LogLevel.Information, Message = "Refused upload {UploadId} ({FileName}): {Reason}")]
    private partial void LogUploadRefused(Guid uploadId, string fileName, string reason);
}
