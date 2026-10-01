// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

// PostgreSQL databases from before its twelve migrations became one. Their history names the twelve.
public sealed class PostgreSqlHistoryTests
{
    private const string FirstOfTheTwelve = "20260910210834_InitialSchema";
    private const string LastOfTheTwelve = "20260928002610_M7FlowBuilder";

    [Fact]
    public async Task ADatabaseAtTheLastOfTheTwelveGoesOnFromTheOneThereIsNow()
    {
        await using TestDatabaseServer server = await TestDatabaseServer.StartAsync(DatabaseProvider.PostgreSql);
        string userName = await LeaveHistoryAsync(server, FirstOfTheTwelve, LastOfTheTwelve);

        using DatabaseServerApplication application = new(server);
        using HttpClient client = application.CreateClient();

        Assert.Equal(
            await application.QueryAsync(database => Task.FromResult(database.Database.GetMigrations())),
            await application.QueryAsync(database => database.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)));
        Assert.True(await application.QueryAsync(database => database.Users.AnyAsync(user => user.UserName == userName, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task ADatabaseThatStoppedBeforeTheLastOfTheTwelveIsRefused()
    {
        await using TestDatabaseServer server = await TestDatabaseServer.StartAsync(DatabaseProvider.PostgreSql);
        await LeaveHistoryAsync(server, FirstOfTheTwelve);

        using DatabaseServerApplication application = new(server);
        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => application.CreateClient());

        Assert.Contains(LastOfTheTwelve, refusal.Message, StringComparison.Ordinal);
    }

    // Today's tables with the history an earlier build wrote. Returns a user the database holds.
    private static async Task<string> LeaveHistoryAsync(TestDatabaseServer server, params string[] migrations)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using DatabaseServerApplication application = new(server);
        string userName = await application.CreateUserAsync(Authentication.DdtRoleNames.Viewer);

        await application.QueryAsync(async database =>
        {
            await database.Database.ExecuteSqlRawAsync("DELETE FROM ddt.\"__EFMigrationsHistory\"", cancellationToken);

            foreach (string migration in migrations)
            {
                await database.Database.ExecuteSqlAsync(
                    $"INSERT INTO ddt.\"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({migration}, '10.0.12')",
                    cancellationToken);
            }

            return 0;
        });

        return userName;
    }
}
