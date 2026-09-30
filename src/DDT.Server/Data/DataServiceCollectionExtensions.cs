// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

        string? postgres = configuration.GetConnectionString("ddtdb");

        // Sees every save. It pushes the audit rows the save added and names an API token that acted.
        services.AddHttpContextAccessor();
        services.AddSingleton<AuditInterceptor>();

        // Deletes a run's credentials in the save that ends the run.
        services.AddSingleton<RunCredentialCleanup>();

        if (string.IsNullOrWhiteSpace(postgres))
        {
            Directory.CreateDirectory(options.StorePath);
            string file = Path.Combine(options.StorePath, "ddt-dev.db");
            services.AddDbContextPool<DdtDbContext>((provider, db) => db
                .UseSqlite($"Data Source={file}")
                // One warning per table on every start, into the event log
                .ConfigureWarnings(warnings => warnings.Ignore(SqliteEventId.SchemaConfiguredWarning))
                .AddInterceptors(provider.GetRequiredService<AuditInterceptor>(), provider.GetRequiredService<RunCredentialCleanup>()));
        }
        else
        {
            services.AddDbContextPool<DdtDbContext>((provider, db) => db
                .UseNpgsql(postgres, npgsql => npgsql
                    .MigrationsHistoryTable("__EFMigrationsHistory", DdtDbContext.Schema)
                    .MigrationsAssembly(typeof(DdtDbContext).Assembly.GetName().Name)
                    .EnableRetryOnFailure())
                .AddInterceptors(provider.GetRequiredService<AuditInterceptor>(), provider.GetRequiredService<RunCredentialCleanup>()));
        }

        services.AddHostedService<DatabaseInitializer>();

        return services;
    }
}
