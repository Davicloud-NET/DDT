// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// Stores from before SQLite was migrated: the file was ddt-dev.db, made from the model, with a fingerprint of its
// tables in user_version and no migration history.
public sealed class SqliteStoreTests : IDisposable
{
    // What the last build without migrations stamped its files with
    private const int LastCreatedSchema = 1008346332;

    private readonly string _store = Path.Combine(Path.GetTempPath(), "ddt-sqlite-store-" + Guid.NewGuid().ToString("N"));

    private string OldFile => Path.Combine(_store, "ddt-dev.db");

    private string File => Path.Combine(_store, "ddt.db");

    [Fact]
    public async Task AFileFromBeforeTheMigrationsGoesOnUnderTheNewNameWithWhatItHeld()
    {
        await CreateOldFileAsync(LastCreatedSchema);

        using SettingsApplication application = new(("DDT:StorePath", _store));
        using HttpClient client = application.CreateClient();

        Assert.True(System.IO.File.Exists(File));
        Assert.False(System.IO.File.Exists(OldFile));
        Assert.True(await application.QueryAsync(database => database.Users.AnyAsync(user => user.UserName == "kept", TestContext.Current.CancellationToken)));
        Assert.Equal(
            await application.QueryAsync(database => Task.FromResult(database.Database.GetMigrations())),
            await application.QueryAsync(database => database.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AFileWithOtherTablesIsRefusedAndStaysAsItIs()
    {
        await CreateOldFileAsync(LastCreatedSchema - 1);

        using SettingsApplication application = new(("DDT:StorePath", _store));
        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => application.CreateClient());

        Assert.Contains("Delete the file and start again", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(await AppliedAsync(File));
    }

    [Fact]
    public async Task AStoreThatHasBothFilesKeepsTheNewOne()
    {
        await CreateOldFileAsync(LastCreatedSchema);

        using (SettingsApplication first = new(("DDT:StorePath", _store)))
        {
            using HttpClient client = first.CreateClient();
        }

        ClearPool(File);
        System.IO.File.Copy(File, OldFile);

        using SettingsApplication second = new(("DDT:StorePath", _store));
        using HttpClient again = second.CreateClient();

        Assert.True(System.IO.File.Exists(OldFile));
        Assert.True(await second.QueryAsync(database => database.Users.AnyAsync(user => user.UserName == "kept", TestContext.Current.CancellationToken)));
    }

    public void Dispose()
    {
        ClearPool(File);
        ClearPool(OldFile);

        if (Directory.Exists(_store))
        {
            Directory.Delete(_store, recursive: true);
        }
    }

    // The tables of the first migration without its history, as EnsureCreated left them.
    private async Task CreateOldFileAsync(int stamp)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_store);

        await using SqliteDdtDbContext old = Context(OldFile);
        await old.GetService<IMigrator>().MigrateAsync(old.Database.GetMigrations().First(), cancellationToken);
        old.Users.Add(new DdtUser { UserName = "kept", NormalizedUserName = "KEPT", CreatedUtc = DateTimeOffset.UtcNow });
        await old.SaveChangesAsync(cancellationToken);
        await old.Database.ExecuteSqlRawAsync("DROP TABLE \"__EFMigrationsHistory\"", cancellationToken);
        await old.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"__EFMigrationsLock\"", cancellationToken);
        // A PRAGMA takes no parameter
        string stamped = string.Create(CultureInfo.InvariantCulture, $"PRAGMA user_version = {stamp}");
        await old.Database.ExecuteSqlRawAsync(stamped, cancellationToken);
        ClearPool(OldFile);
    }

    private static async Task<IEnumerable<string>> AppliedAsync(string file)
    {
        await using SqliteDdtDbContext context = Context(file);
        IEnumerable<string> applied = await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);
        ClearPool(file);

        return applied;
    }

    private static SqliteDdtDbContext Context(string file)
    {
        ServiceCollection services = new();
        services.AddOptions<IdentityOptions>().Configure(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3);
        DbContextOptionsBuilder<SqliteDdtDbContext> options = new DbContextOptionsBuilder<SqliteDdtDbContext>()
            .UseApplicationServiceProvider(services.BuildServiceProvider());
        options.UseDdtSqlite(file);

        return new SqliteDdtDbContext(options.Options);
    }

    // Only this file's pool: clearing every pool disposes connections other test classes are opening at that moment.
    private static void ClearPool(string file)
    {
        using SqliteConnection connection = new($"Data Source={file}");
        SqliteConnection.ClearPool(connection);
    }
}
