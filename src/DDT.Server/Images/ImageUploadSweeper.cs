using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Images;

// Removes what abandoned uploads leave behind: open sessions nobody continued for a day with their part files,
// completed sessions a day after they finished, and part files that belong to no session. The library itself is
// never swept.
public sealed partial class ImageUploadSweeper(
    IServiceScopeFactory scopes,
    ImageStore store,
    ImageUploadLocks locks,
    TimeProvider timeProvider,
    ILogger<ImageUploadSweeper> logger) : BackgroundService
{
    private static readonly TimeSpan s_interval = TimeSpan.FromHours(1);

    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset cutoff = timeProvider.GetUtcNow() - ImageUploadLimits.SessionLifetime;

        // Listed before the sessions are read: a part file created after this list cannot be taken for an orphan
        // just because its session was created after the read.
        List<(Guid UploadId, FileInfo File)> parts = ListParts();

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();

        // SQLite cannot compare DateTimeOffset, so ages are judged here.
        var sessions = await database.ImageUploads
            .AsNoTracking()
            .Select(u => new { u.Id, u.UpdatedUtc, Completed = u.CompletedSha256 != null })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int removed = 0;

        foreach (Guid uploadId in sessions.Where(s => !s.Completed && s.UpdatedUtc < cutoff).Select(s => s.Id))
        {
            if (await RemoveAbandonedAsync(database, uploadId, cutoff, cancellationToken).ConfigureAwait(false))
            {
                removed++;
            }
        }

        Guid[] finished = [.. sessions.Where(s => s.Completed && s.UpdatedUtc < cutoff).Select(s => s.Id)];

        if (finished.Length > 0)
        {
            removed += await database.ImageUploads
                .Where(u => finished.Contains(u.Id) && u.CompletedSha256 != null)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        HashSet<Guid> known = [.. sessions.Select(s => s.Id)];

        foreach ((Guid uploadId, FileInfo file) in parts)
        {
            if (!known.Contains(uploadId) && file.LastWriteTimeUtc < cutoff.UtcDateTime)
            {
                file.Delete();
                removed++;
            }
        }

        if (removed > 0)
        {
            LogSwept(removed);
        }

        return removed;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(s_interval, timeProvider);

        do
        {
            // A failed pass must not stop the host, which also runs the web UI and the pxe role.
            try
            {
                await SweepOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogSweepFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    // A session that a request is using is left for the next pass. The row is read again under the lock, because
    // a chunk may have arrived since the list was read.
    private async Task<bool> RemoveAbandonedAsync(DdtDbContext database, Guid uploadId, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        if (!locks.TryEnter(uploadId, out ImageUploadLock? held))
        {
            return false;
        }

        using (held)
        {
            ImageUpload? upload = await database.ImageUploads
                .FirstOrDefaultAsync(u => u.Id == uploadId, cancellationToken)
                .ConfigureAwait(false);

            if (upload is null || upload.CompletedSha256 is not null || upload.UpdatedUtc >= cutoff)
            {
                return false;
            }

            database.ImageUploads.Remove(upload);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            File.Delete(store.PartPath(uploadId));

            return true;
        }
    }

    private List<(Guid UploadId, FileInfo File)> ListParts()
    {
        DirectoryInfo uploads = new(store.UploadsDirectory);

        if (!uploads.Exists)
        {
            return [];
        }

        List<(Guid UploadId, FileInfo File)> parts = [];

        foreach (FileInfo file in uploads.EnumerateFiles("*.part"))
        {
            if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(file.Name), "N", out Guid uploadId))
            {
                parts.Add((uploadId, file));
            }
        }

        return parts;
    }

    [LoggerMessage(EventId = 910, Level = LogLevel.Information, Message = "Removed {Count} abandoned or finished image uploads")]
    private partial void LogSwept(int count);

    [LoggerMessage(EventId = 911, Level = LogLevel.Warning, Message = "Could not remove abandoned image uploads")]
    private partial void LogSweepFailed(Exception exception);
}
