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

        if (string.IsNullOrWhiteSpace(postgres))
        {
            Directory.CreateDirectory(options.StorePath);
            string file = Path.Combine(options.StorePath, "ddt-dev.db");
            services.AddDbContextPool<DdtDbContext>(db => db.UseSqlite($"Data Source={file}"));
        }
        else
        {
            services.AddDbContextPool<DdtDbContext>(db => db.UseNpgsql(postgres, npgsql => npgsql
                .MigrationsHistoryTable("__EFMigrationsHistory", DdtDbContext.Schema)
                .MigrationsAssembly(typeof(DdtDbContext).Assembly.GetName().Name)
                .EnableRetryOnFailure()));
        }

        services.AddHostedService<DatabaseInitializer>();

        return services;
    }
}
