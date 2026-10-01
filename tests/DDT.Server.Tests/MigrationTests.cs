// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

public sealed class MigrationTests
{
    // Most tests run on SQLite. So only this one notices a model change that nobody generated a migration for on each
    // of the other databases, which would stop their first start. It compares the model with the snapshot and needs
    // no database.
    [Theory]
    [InlineData(DatabaseProvider.Sqlite)]
    [InlineData(DatabaseProvider.PostgreSql)]
    [InlineData(DatabaseProvider.SqlServer)]
    public void TheMigrationsMatchTheModel(DatabaseProvider provider)
    {
        using DdtDbContext context = Context(provider);

        Assert.False(context.Database.HasPendingModelChanges());
        Assert.NotEmpty(context.Database.GetMigrations());
    }

    // SQL Server allows one path from a user to a table, so it gets none of these and DDT clears them itself.
    [Fact]
    public void OnlySqlServerLeavesClearingAUsersReferencesToDdt()
    {
        Assert.Equal(
            [0, 0, 13],
            new[] { DatabaseProvider.Sqlite, DatabaseProvider.PostgreSql, DatabaseProvider.SqlServer }.Select(provider =>
            {
                using DdtDbContext context = Context(provider);

                return context.Model.GetEntityTypes()
                    .SelectMany(entity => entity.GetForeignKeys())
                    .Count(key => key.PrincipalEntityType.ClrType == typeof(DdtUser) && key.DeleteBehavior == DeleteBehavior.ClientSetNull);
            }));
    }

    private static DdtDbContext Context(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.PostgreSql => new PostgreSqlDdtDbContextFactory().CreateDbContext([]),
        DatabaseProvider.SqlServer => new SqlServerDdtDbContextFactory().CreateDbContext([]),
        _ => new SqliteDdtDbContextFactory().CreateDbContext([]),
    };
}
