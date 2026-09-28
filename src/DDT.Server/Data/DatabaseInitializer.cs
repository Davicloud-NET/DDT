// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Data;

// Both the web and pxe roles may start against the same database. Npgsql takes an ACCESS
// EXCLUSIVE lock on the migration history table, which serialises concurrent starts.
public sealed partial class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        DdtDbContext context = scope.ServiceProvider.GetRequiredService<DdtDbContext>();

        if (context.Database.IsNpgsql())
        {
            LogApplyingMigrations();
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            LogMigrationsApplied();
            return;
        }

        LogCreatingDevelopmentSchema();

        // EnsureCreated leaves an existing file alone. That file then lacks new tables and fails far from the cause. A
        // schema fingerprint in SQLite's user_version catches every change, and EnsureCreated never touches it.
        int fingerprint = SchemaFingerprint(context);

        if (await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false))
        {
            string stamp = string.Create(CultureInfo.InvariantCulture, $"PRAGMA user_version = {fingerprint}");
            await context.Database.ExecuteSqlRawAsync(stamp, cancellationToken).ConfigureAwait(false);
            return;
        }

        List<int> stored = await context.Database
            .SqlQuery<int>($"SELECT user_version AS Value FROM pragma_user_version")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (stored.Single() != fingerprint)
        {
            List<string> missing = await FindMissingAsync(context, cancellationToken).ConfigureAwait(false);
            string difference = missing.Count > 0 ? $"lacks {string.Join(", ", missing)}" : "does not match the current model";

            throw new InvalidOperationException(
                $"The development database {context.Database.GetDbConnection().DataSource} was created by another build " +
                $"and {difference}. Delete the file and start again; its accounts and machines are lost, and a new " +
                "administrator password is printed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static int SchemaFingerprint(DbContext context)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(context.Database.GenerateCreateScript()));
        int value = BitConverter.ToInt32(hash) & int.MaxValue;

        // Zero is what a file that was never stamped reports.
        return value == 0 ? 1 : value;
    }

    // Only used for the error message. It names the tables and columns an operator will recognise.
    private static async Task<List<string>> FindMissingAsync(DdtDbContext context, CancellationToken cancellationToken)
    {
        List<string> missing = [];

        foreach (IEntityType entity in context.Model.GetEntityTypes())
        {
            if (entity.GetTableName() is not { } table)
            {
                continue;
            }

            StoreObjectIdentifier store = StoreObjectIdentifier.Table(table, entity.GetSchema());

            List<string> columns = await context.Database
                .SqlQuery<string>($"SELECT name AS Value FROM pragma_table_info({table})")
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (columns.Count == 0)
            {
                missing.Add(table);
                continue;
            }

            missing.AddRange(entity.GetProperties()
                .Select(property => property.GetColumnName(store))
                .OfType<string>()
                .Where(column => !columns.Contains(column, StringComparer.OrdinalIgnoreCase))
                .Select(column => $"{table}.{column}"));
        }

        return [.. missing.Distinct()];
    }

    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "Applying database migrations")]
    private partial void LogApplyingMigrations();

    [LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "Database migrations applied")]
    private partial void LogMigrationsApplied();

    [LoggerMessage(EventId = 102, Level = LogLevel.Warning, Message = "Creating the development schema directly. SQLite is not a supported production store.")]
    private partial void LogCreatingDevelopmentSchema();
}
