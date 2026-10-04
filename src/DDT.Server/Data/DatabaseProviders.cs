// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DDT.Server.Data;

// How each database is opened. dotnet ef and the server go through here, so migrations are written for the same
// options they run with.
public static class DatabaseProviders
{
    public const string ConnectionStringName = "ddtdb";

    private const string HistoryTable = "__EFMigrationsHistory";

    public static string SqliteFileIn(string storePath) => SqliteStore.PathIn(storePath);

    // Without DDT:Database, a connection string means PostgreSQL, as it did before there was a choice.
    public static DatabaseProvider Choose(string? configured, string? connection)
    {
        bool connects = !string.IsNullOrWhiteSpace(connection);

        if (string.IsNullOrWhiteSpace(configured))
        {
            return connects ? DatabaseProvider.PostgreSql : DatabaseProvider.Sqlite;
        }

        string[] names = Enum.GetNames<DatabaseProvider>();

        // Matched against the names rather than Enum.TryParse, which also accepts "1".
        if (names.FirstOrDefault(name => name.Equals(configured.Trim(), StringComparison.OrdinalIgnoreCase)) is not { } named)
        {
            throw new InvalidOperationException($"DDT:Database is {configured}. It has to be one of {string.Join(", ", names)}.");
        }

        DatabaseProvider provider = Enum.Parse<DatabaseProvider>(named);

        if (connects == (provider == DatabaseProvider.Sqlite))
        {
            throw new InvalidOperationException(provider == DatabaseProvider.Sqlite
                ? "DDT:Database is Sqlite, which is a file in the store, and ConnectionStrings:ddtdb names another database. Remove one of them."
                : $"DDT:Database is {provider}, which needs ConnectionStrings:ddtdb.");
        }

        return provider;
    }

    public static DbContextOptionsBuilder UseDdtSqlite(this DbContextOptionsBuilder database, string file)
    {
        ArgumentNullException.ThrowIfNull(database);

        return database
            .UseSqlite($"Data Source={file}")
            // SQLite has no schemas, and says so for every table on every start
            .ConfigureWarnings(warnings => warnings.Ignore(SqliteEventId.SchemaConfiguredWarning));
    }

    public static DbContextOptionsBuilder UseDdtPostgreSql(this DbContextOptionsBuilder database, string connection)
    {
        ArgumentNullException.ThrowIfNull(database);

        return database.UseNpgsql(connection, npgsql => npgsql
            .MigrationsHistoryTable(HistoryTable, DdtDbContext.Schema)
            .EnableRetryOnFailure());
    }

    public static DbContextOptionsBuilder UseDdtSqlServer(this DbContextOptionsBuilder database, string connection)
    {
        ArgumentNullException.ThrowIfNull(database);

        return database.UseSqlServer(connection, sqlServer => sqlServer
            .MigrationsHistoryTable(HistoryTable, DdtDbContext.Schema)
            .EnableRetryOnFailure());
    }
}
