// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Data;

// Brings the tables up to date before anything else starts. The web and pxe roles and the console verbs may start
// against the same database at once: EF Core takes a lock in the database for the migration, which makes them wait.
public sealed partial class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        DdtDbContext context = scope.ServiceProvider.GetRequiredService<DdtDbContext>();

        bool adopted = context.Database.IsSqlite()
            ? await SqliteStore.AdoptAsync(context, cancellationToken).ConfigureAwait(false)
            : context.Database.IsNpgsql() && await PostgreSqlHistory.AdoptAsync(context, cancellationToken).ConfigureAwait(false);

        if (adopted)
        {
            LogAdopted(MigrationHistory.First(context));
        }

        LogApplyingMigrations();
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        LogMigrationsApplied();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "Applying database migrations")]
    private partial void LogApplyingMigrations();

    [LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "Database migrations applied")]
    private partial void LogMigrationsApplied();

    [LoggerMessage(EventId = 103, Level = LogLevel.Information, Message = "The database has its tables from an earlier build. Its migrations go on from {Migration}")]
    private partial void LogAdopted(string migration);
}
