// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers;
using DDT.Contracts.Images;
using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Images;

// Completing an upload runs separately from its request, in ImageUploadCompleter.
public sealed class ImageUploadSessions(
    DdtDbContext database,
    ImageStore store,
    ImageUploadLocks locks,
    TimeProvider timeProvider)
{
    private const int CopyBufferBytes = 1024 * 1024;

    public async Task<UploadCreation> FindOrCreateAsync(CreateImageUploadRequest request, Guid? userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Runs under the library lock. That way two requests for the same file find the same session. And two new
        // sessions can't both pass the free space check that should count the other one.
        await using SemaphoreHold hold = await store.LibraryLock.EnterAsync(cancellationToken).ConfigureAwait(false);

        List<ImageUpload> open = await database.ImageUploads
            .Where(u => u.CompletedSha256 == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The same file selected again, after a reload or in a second tab, continues where it stopped.
        ImageUpload? found = open.FirstOrDefault(u =>
            u.FileName == request.FileName
            && u.Length == request.Length
            && u.LastModified == request.LastModified
            && u.Kind == request.Kind);

        if (found is not null)
        {
            return new UploadCreation(Session(found), Created: false, 0, 0);
        }

        // Sum as decimal. Nothing limits how many sessions are open, so a long could overflow.
        decimal required = request.Length + open.Sum(u => (decimal)(u.Length - u.Offset)) + ImageUploadLimits.FreeSpaceMargin;
        long available = store.Volume().AvailableFreeSpace;

        if (available < required)
        {
            return new UploadCreation(null, Created: false, (long)Math.Min(required, long.MaxValue), available);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        ImageUpload upload = new()
        {
            Id = Guid.CreateVersion7(now),
            FileName = request.FileName,
            Length = request.Length,
            LastModified = request.LastModified,
            Kind = request.Kind,
            CreatedByUserId = userId,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        database.ImageUploads.Add(upload);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new UploadCreation(Session(upload), Created: true, 0, 0);
    }

    public async Task<IReadOnlyList<ImageUploadSession>> ListOpenAsync(CancellationToken cancellationToken)
    {
        List<ImageUpload> open = await database.ImageUploads
            .AsNoTracking()
            .Where(u => u.CompletedSha256 == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // SQLite cannot order by DateTimeOffset.
        return [.. open.OrderBy(u => u.CreatedUtc).ThenBy(u => u.Id).Select(Session)];
    }

    public async Task<UploadAppend> AppendAsync(Guid uploadId, long offset, long length, Stream body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        ImageUpload? seen = await database.ImageUploads
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == uploadId, cancellationToken)
            .ConfigureAwait(false);

        if (seen is null)
        {
            return new UploadAppend(UploadAppendStatus.NotFound, 0);
        }

        if (offset > seen.Length - length)
        {
            return new UploadAppend(UploadAppendStatus.BeyondLength, seen.Offset);
        }

        if (seen.CompletedSha256 is not null)
        {
            return new UploadAppend(UploadAppendStatus.Completed, seen.Offset);
        }

        if (!locks.TryEnter(uploadId, out ImageUploadLock? held))
        {
            return new UploadAppend(UploadAppendStatus.Busy, seen.Offset);
        }

        using (held)
        {
            // Read it again under the lock. Whoever held it before may have moved the offset or finished the upload.
            ImageUpload? upload = await database.ImageUploads
                .FirstOrDefaultAsync(u => u.Id == uploadId, cancellationToken)
                .ConfigureAwait(false);

            if (upload is null)
            {
                return new UploadAppend(UploadAppendStatus.NotFound, 0);
            }

            if (upload.CompletedSha256 is not null)
            {
                return new UploadAppend(UploadAppendStatus.Completed, upload.Offset);
            }

            if (offset != upload.Offset)
            {
                return new UploadAppend(UploadAppendStatus.OffsetMismatch, upload.Offset);
            }

            return await WriteChunkAsync(upload, length, body, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<UploadDiscardStatus> DiscardAsync(Guid uploadId, CancellationToken cancellationToken)
    {
        if (!locks.TryEnter(uploadId, out ImageUploadLock? held))
        {
            return UploadDiscardStatus.Busy;
        }

        using (held)
        {
            ImageUpload? upload = await database.ImageUploads
                .FirstOrDefaultAsync(u => u.Id == uploadId, cancellationToken)
                .ConfigureAwait(false);

            if (upload is null)
            {
                return UploadDiscardStatus.NotFound;
            }

            database.ImageUploads.Remove(upload);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            File.Delete(store.PartPath(uploadId));

            return UploadDiscardStatus.Discarded;
        }
    }

    private async Task<UploadAppend> WriteChunkAsync(ImageUpload upload, long length, Stream body, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(store.UploadsDirectory);

        // No buffer, so after a failed write there's nothing left that closing the file would try to write again.
        await using (FileStream part = new(
            store.PartPath(upload.Id),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 0,
            FileOptions.Asynchronous))
        {
            // Bytes below the committed offset were acknowledged, so a shorter file has lost some. Extending it would
            // fill the gap with zeros. Instead, the client sends the file again from the start.
            if (part.Length < upload.Offset)
            {
                upload.Offset = 0;
                upload.UpdatedUtc = timeProvider.GetUtcNow();
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return new UploadAppend(UploadAppendStatus.Restarted, 0);
            }

            // Drops the tail of a chunk that was cut off or written before a crash and never committed.
            part.SetLength(upload.Offset);
            part.Position = upload.Offset;

            if (await CopyAsync(body, part, length, cancellationToken).ConfigureAwait(false) is { } failure)
            {
                return new UploadAppend(failure, upload.Offset);
            }
        }

        upload.Offset += length;
        upload.UpdatedUtc = timeProvider.GetUtcNow();

        // The chunk is on disk, so record it even if the client has gone. A retry then continues from the new offset.
        await database.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

        return new UploadAppend(UploadAppendStatus.Appended, upload.Offset);
    }

    private async Task<UploadAppendStatus?> CopyAsync(Stream body, FileStream part, long length, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferBytes);
        using CancellationTokenSource stall = new(ImageUploadLimits.NoProgressTimeout, timeProvider);
        using CancellationTokenSource reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stall.Token);

        try
        {
            for (long received = 0; received < length;)
            {
                int count;

                try
                {
                    count = await body
                        .ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - received)), reading.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stall.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    return UploadAppendStatus.Stalled;
                }
                catch (IOException)
                {
                    // Kestrel reports a client that went away, or sent less than it announced, as an IOException.
                    return UploadAppendStatus.CutOff;
                }

                if (count == 0)
                {
                    return UploadAppendStatus.CutOff;
                }

                stall.CancelAfter(ImageUploadLimits.NoProgressTimeout);
                await part.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                received += count;
            }

            // Flush to disk before saving the offset that promises these bytes. Then a power loss can't leave a gap.
            part.Flush(flushToDisk: true);

            return null;
        }
        catch (IOException exception) when (IsDiskFull(exception))
        {
            return UploadAppendStatus.DiskFull;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return UploadAppendStatus.CutOff;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // ERROR_DISK_FULL and ERROR_HANDLE_DISK_FULL on Windows. On Unix the HResult is the errno, and 28 is ENOSPC.
    private static bool IsDiskFull(IOException exception) =>
        exception.HResult is unchecked((int)0x80070070) or unchecked((int)0x80070027) or 28;

    private static ImageUploadSession Session(ImageUpload upload) =>
        new(upload.Id, upload.FileName, upload.Length, upload.LastModified, upload.Offset, ImageUploadLimits.ChunkBytes, upload.Kind);
}
