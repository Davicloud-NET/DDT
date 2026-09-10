using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Data;

// Without UseApplicationServiceProvider the design time model falls back to Identity schema
// version 1, which silently drops the passkey table from generated migrations.
public sealed class DdtDbContextFactory : IDesignTimeDbContextFactory<DdtDbContext>
{
    public DdtDbContext CreateDbContext(string[] args)
    {
        ServiceCollection services = new();
        services.AddOptions<IdentityOptions>()
            .Configure(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3);

        ServiceProvider applicationServices = services.BuildServiceProvider();

        string connectionString =
            Environment.GetEnvironmentVariable("DDT_DESIGN_TIME_CONNECTION")
            ?? "Host=localhost;Database=ddt;Username=ddt;Password=ddt";

        DbContextOptions<DdtDbContext> options = new DbContextOptionsBuilder<DdtDbContext>()
            .UseApplicationServiceProvider(applicationServices)
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__EFMigrationsHistory", DdtDbContext.Schema)
                .MigrationsAssembly(typeof(DdtDbContextFactory).Assembly.GetName().Name))
            .Options;

        return new DdtDbContext(options);
    }
}
