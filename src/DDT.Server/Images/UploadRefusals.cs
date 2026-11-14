// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Concurrent;
using DDT.Contracts.Messages;
using DDT.Server.Data;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Images;

// A refused upload loses its session, so the reason is kept here for a retry that can't find the session any more. The
// same goes for an upload that ends without a library entry. It logs under ImageUploadCompleter's category, because the
// logging settings may name that category.
public sealed partial class UploadRefusals(ImageStore store, TimeProvider timeProvider, ILogger<ImageUploadCompleter> logger)
{
    private readonly ConcurrentDictionary<Guid, (UploadCompletion Completion, DateTimeOffset EndedUtc)> _ended = new();

    public UploadCompletion Missing(Guid uploadId) =>
        _ended.TryGetValue(uploadId, out (UploadCompletion Completion, DateTimeOffset EndedUtc) ended)
            ? ended.Completion
            : new UploadCompletion(UploadCompletionStatus.NotFound, []);

    public async Task<UploadCompletion> RefuseAsync(DdtDbContext database, ImageUpload upload, ServerMessage refusal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(refusal);

        UploadCompletion refused = await EndAsync(
            database,
            upload,
            new UploadCompletion(UploadCompletionStatus.Refused, [], Refusal: refusal),
            cancellationToken).ConfigureAwait(false);

        LogUploadRefused(upload.Id, upload.FileName, refusal.Text);

        return refused;
    }

    // Removes the session and what is left of its files, and keeps the completion for a retry.
    public async Task<UploadCompletion> EndAsync(DdtDbContext database, ImageUpload upload, UploadCompletion completion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(completion);

        Remember(upload.Id, completion);
        database.ImageUploads.Remove(upload);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (string file in store.UploadFiles(upload.Id))
        {
            File.Delete(file);
        }

        return completion;
    }

    // Record the completion before the session is removed, so a retry that can't find the session finds it instead.
    // It's kept as long as the session could have lived.
    private void Remember(Guid uploadId, UploadCompletion completion)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        foreach (KeyValuePair<Guid, (UploadCompletion Completion, DateTimeOffset EndedUtc)> ended in _ended)
        {
            if (now - ended.Value.EndedUtc >= ImageUploadLimits.SessionLifetime)
            {
                _ended.TryRemove(ended);
            }
        }

        _ended[uploadId] = (completion, now);
    }

    [LoggerMessage(EventId = 902, Level = LogLevel.Information, Message = "Refused upload {UploadId} ({FileName}): {Reason}")]
    private partial void LogUploadRefused(Guid uploadId, string fileName, string reason);
}
