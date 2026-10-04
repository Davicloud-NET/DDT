// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Host.Startup;
using DDT.Server.Authentication;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DDT.Server.Tests;

public sealed class BackupConsoleTests : IDisposable
{
    private readonly DdtApplication _application = new();

    [Fact]
    public async Task ItCopiesTheDatabaseOfARunningServerIntoANewFile()
    {
        string userName = await _application.CreateUserAsync(DdtRoleNames.Viewer);
        string copy = Path.Combine(_application.StorePath, "copies", "ddt-backup.db");
        Directory.CreateDirectory(Path.GetDirectoryName(copy) ?? _application.StorePath);
        using StringWriter output = new();

        Assert.Equal(0, BackupConsole.Run(["backup", copy], output, Configuration()));
        Assert.Contains(copy, output.ToString(), StringComparison.Ordinal);

        await using (SqliteConnection connection = new($"Data Source={copy};Mode=ReadOnly;Pooling=False"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqliteCommand count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM AspNetUsers WHERE UserName = $name";
            count.Parameters.AddWithValue("$name", userName);

            Assert.Equal(1L, await count.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        }

        // A second run doesn't write over the first
        Assert.Equal(1, BackupConsole.Run(["backup", copy], output, Configuration()));
        Assert.Contains("exists already", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ADatabaseServerIsLeftToItsOwnTools()
    {
        using StringWriter output = new();
        IConfiguration configuration = Configuration(("ConnectionStrings:ddtdb", "Host=db;Database=ddt"));

        Assert.Equal(1, BackupConsole.Run(["backup", Path.Combine(_application.StorePath, "never.db")], output, configuration));
        Assert.Contains("PostgreSql", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAFileItPrintsTheUsage()
    {
        using StringWriter output = new();

        Assert.Equal(2, BackupConsole.Run(["backup"], output, Configuration()));
        Assert.StartsWith("Usage", output.ToString(), StringComparison.Ordinal);
    }

    public void Dispose() => _application.Dispose();

    private IConfiguration Configuration(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection([new("DDT:StorePath", _application.StorePath), .. settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value))])
            .Build();
}
