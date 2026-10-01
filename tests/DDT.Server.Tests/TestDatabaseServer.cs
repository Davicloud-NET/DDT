// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

namespace DDT.Server.Tests;

// A PostgreSQL or SQL Server for one test, with a database of its own. PostgreSQL comes from Docker. SQL Server comes
// from DDT_TEST_SQLSERVER, a connection string without a database, then from LocalDB on Windows, then from Docker.
// DDT_TEST_SQLSERVER=container goes straight to Docker.
public sealed class TestDatabaseServer : IAsyncDisposable
{
    private const string LocalDb = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true";

    private readonly IAsyncDisposable? _container;
    private readonly string? _dropWith;

    private TestDatabaseServer(DatabaseProvider provider, string connectionString, IAsyncDisposable? container, string? dropWith)
    {
        Provider = provider;
        ConnectionString = connectionString;
        _container = container;
        _dropWith = dropWith;
    }

    public DatabaseProvider Provider { get; }

    public string ConnectionString { get; }

    // Skips the test that asked when there is no such server here.
    public static async Task<TestDatabaseServer> StartAsync(DatabaseProvider provider)
    {
        string? named = Environment.GetEnvironmentVariable("DDT_TEST_SQLSERVER");

        TestDatabaseServer? server = provider != DatabaseProvider.SqlServer || named == "container"
            ? await ContainerAsync(provider)
            : Existing(named) ?? LocalDbServer() ?? await ContainerAsync(provider);

        Assert.SkipWhen(server is null, $"There is no {provider} to test against. Start Docker to run this test.");

        return server;
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        if (_dropWith is not null)
        {
            string name = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;
            SqlConnection.ClearAllPools();

            await using SqlConnection connection = new(_dropWith);
            await connection.OpenAsync();
            await using SqlCommand drop = connection.CreateCommand();
            drop.CommandText = $"IF DB_ID('{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static TestDatabaseServer? Existing(string? server) =>
        string.IsNullOrWhiteSpace(server)
            ? null
            : new TestDatabaseServer(DatabaseProvider.SqlServer, WithOwnDatabase(server), null, server);

    private static TestDatabaseServer? LocalDbServer()
    {
        string tools = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft SQL Server");

        if (!OperatingSystem.IsWindows() || !Directory.Exists(tools) || Directory.GetFiles(tools, "SqlLocalDB.exe", SearchOption.AllDirectories).Length == 0)
        {
            return null;
        }

        return new TestDatabaseServer(DatabaseProvider.SqlServer, WithOwnDatabase(LocalDb), null, LocalDb);
    }

    private static string WithOwnDatabase(string server) =>
        new SqlConnectionStringBuilder(server) { InitialCatalog = "ddt-test-" + Guid.NewGuid().ToString("N") }.ConnectionString;

    private static async Task<TestDatabaseServer?> ContainerAsync(DatabaseProvider provider)
    {
        IAsyncDisposable? container = null;

        try
        {
            if (provider == DatabaseProvider.SqlServer)
            {
                MsSqlContainer sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
                container = sqlServer;
                await sqlServer.StartAsync(TestContext.Current.CancellationToken);

                return new TestDatabaseServer(provider, WithOwnDatabase(sqlServer.GetConnectionString()), container, null);
            }

            PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
            container = postgres;
            await postgres.StartAsync(TestContext.Current.CancellationToken);

            return new TestDatabaseServer(provider, postgres.GetConnectionString(), container, null);
        }
        catch (DockerUnavailableException)
        {
            if (container is not null)
            {
                await container.DisposeAsync();
            }

            return null;
        }
    }
}
