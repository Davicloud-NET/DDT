// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Packages;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Packages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Applications;

// Removes application files that no application names a day after their upload: the page that uploaded them was never
// saved. Files a run still downloads are left for a later pass.
public sealed partial class ApplicationFilesSweeper(
    IServiceScopeFactory scopes,
    ImageStore store,
    TimeProvider timeProvider,
    ILogger<ApplicationFilesSweeper> logger) : BackgroundService
{
    public static readonly TimeSpan UnsavedLifetime = TimeSpan.FromDays(1);

    private static readonly TimeSpan s_interval = TimeSpan.FromHours(1);

    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset cutoff = timeProvider.GetUtcNow() - UnsavedLifetime;

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();

        // Under the lock, so an application saved meanwhile can't name files that are being removed.
        await using SemaphoreHold hold = await store.LibraryLock.EnterAsync(cancellationToken).ConfigureAwait(false);

        // SQLite can't compare DateTimeOffset, so the ages are checked here instead of in the query.
        List<Package> unnamed = await database.Packages
            .Where(p => p.Kind == PackageKind.Application && !database.Applications.Any(a => a.PackageId == p.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        List<Package> removed = [];

        foreach (Package package in unnamed.Where(p => p.UploadedUtc < cutoff))
        {
            if (!await ApplicationFilesRelease.InUseAsync(database, package.Id, cancellationToken).ConfigureAwait(false))
            {
                database.Packages.Remove(package);
                removed.Add(package);
            }
        }

        if (removed.Count == 0)
        {
            return 0;
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (string sha256 in removed.Select(p => p.Sha256).Distinct(StringComparer.Ordinal))
        {
            await store.DeleteObjectIfUnreferencedAsync(database, sha256, CancellationToken.None).ConfigureAwait(false);
        }

        LogSwept(removed.Count);

        return removed.Count;
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

    [LoggerMessage(EventId = 1300, Level = LogLevel.Information, Message = "Removed the files of {Count} application uploads that no application names")]
    private partial void LogSwept(int count);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Warning, Message = "Could not remove the files of unsaved application uploads")]
    private partial void LogSweepFailed(Exception exception);
}
