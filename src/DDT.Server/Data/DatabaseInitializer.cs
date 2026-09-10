using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Data;

// Both the web and pxe roles may start against the same database. Npgsql takes an ACCESS
// EXCLUSIVE lock on the migration history table, which serialises concurrent starts.
public sealed partial class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        DdtDbContext context = scope.ServiceProvider.GetRequiredService<DdtDbContext>();

        if (context.Database.IsNpgsql())
        {
            LogApplyingMigrations();
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            LogMigrationsApplied();
            return;
        }

        LogCreatingDevelopmentSchema();
        await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "Applying database migrations")]
    private partial void LogApplyingMigrations();

    [LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "Database migrations applied")]
    private partial void LogMigrationsApplied();

    [LoggerMessage(EventId = 102, Level = LogLevel.Warning, Message = "Creating the development schema directly. SQLite is not a supported production store.")]
    private partial void LogCreatingDevelopmentSchema();
}
