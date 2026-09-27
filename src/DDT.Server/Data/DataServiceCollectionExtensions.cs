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

        string? postgres = configuration.GetConnectionString("ddtdb");

        // Sees every save, to push the audit rows it added and to name an API token that acted.
        services.AddHttpContextAccessor();
        services.AddSingleton<AuditInterceptor>();

        if (string.IsNullOrWhiteSpace(postgres))
        {
            Directory.CreateDirectory(options.StorePath);
            string file = Path.Combine(options.StorePath, "ddt-dev.db");
            services.AddDbContextPool<DdtDbContext>((provider, db) => db
                .UseSqlite($"Data Source={file}")
                .AddInterceptors(provider.GetRequiredService<AuditInterceptor>()));
        }
        else
        {
            services.AddDbContextPool<DdtDbContext>((provider, db) => db
                .UseNpgsql(postgres, npgsql => npgsql
                    .MigrationsHistoryTable("__EFMigrationsHistory", DdtDbContext.Schema)
                    .MigrationsAssembly(typeof(DdtDbContext).Assembly.GetName().Name)
                    .EnableRetryOnFailure())
                .AddInterceptors(provider.GetRequiredService<AuditInterceptor>()));
        }

        services.AddHostedService<DatabaseInitializer>();

        return services;
    }
}
