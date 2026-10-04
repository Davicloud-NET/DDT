// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Data;

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddDdtData(
        this IServiceCollection services,
        IConfiguration configuration,
        DdtOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        string connection = configuration.GetConnectionString(DatabaseProviders.ConnectionStringName) ?? string.Empty;

        // Sees every save. It pushes the audit rows the save added and names an API token that acted.
        services.AddHttpContextAccessor();
        services.AddSingleton<AuditInterceptor>();

        // Deletes a run's credentials in the save that ends the run.
        services.AddSingleton<RunCredentialCleanup>();

        switch (DatabaseProviders.Choose(options.Database, connection))
        {
            case DatabaseProvider.PostgreSql:
                services.AddDbContextPool<DdtDbContext, PostgreSqlDdtDbContext>((provider, database) =>
                    Intercepted(database.UseDdtPostgreSql(connection), provider));
                break;

            case DatabaseProvider.SqlServer:
                services.AddDbContextPool<DdtDbContext, SqlServerDdtDbContext>((provider, database) =>
                    Intercepted(database.UseDdtSqlServer(connection), provider));
                break;

            default:
                Directory.CreateDirectory(options.StorePath);
                SqliteStore.TakeOverOldFile(options.StorePath);
                string file = SqliteStore.PathIn(options.StorePath);
                services.AddDbContextPool<DdtDbContext, SqliteDdtDbContext>((provider, database) =>
                    Intercepted(database.UseDdtSqlite(file), provider));
                break;
        }

        services.AddHostedService<DatabaseInitializer>();

        return services;
    }

    private static void Intercepted(DbContextOptionsBuilder database, IServiceProvider provider) =>
        database.AddInterceptors(provider.GetRequiredService<AuditInterceptor>(), provider.GetRequiredService<RunCredentialCleanup>());
}
