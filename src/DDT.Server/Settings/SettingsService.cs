// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Settings;

// Registered after DatabaseInitializer, so the schema exists. It imports and loads the store as it starts, before the
// server binds its listeners, so the first request sees the stored settings; then it polls every 15 seconds for other
// processes' saves and applies.
public sealed partial class SettingsService(
    IServiceScopeFactory scopes,
    DdtSettings settings,
    SettingsApplier applier,
    SettingsHostStates hostStates,
    TimeProvider timeProvider,
    ILogger<SettingsService> logger) : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim _refresh = new(1, 1);
    private Dictionary<string, string> _hostStates = new(StringComparer.Ordinal);
    private DateTimeOffset _refreshed;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        using (IServiceScope scope = scopes.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SettingsImporter>().ImportAsync(cancellationToken).ConfigureAwait(false);
            settings.Publish(await scope.ServiceProvider.GetRequiredService<SettingsStore>().LoadAsync(cancellationToken).ConfigureAwait(false));
        }

        hostStates.Changed = PushAsync;
        await applier.StartAsync(cancellationToken).ConfigureAwait(false);
        _refreshed = timeProvider.GetUtcNow();

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        _refresh.Dispose();
        base.Dispose();
    }

    // Reads what changed since the last look without waiting for the timer. A failed look must not stop the host; the
    // snapshot loaded before stays in force.
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _refresh.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using IServiceScope scope = scopes.CreateScope();
            SettingsStore store = scope.ServiceProvider.GetRequiredService<SettingsStore>();
            DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
            SettingsViews views = scope.ServiceProvider.GetRequiredService<SettingsViews>();

            Dictionary<string, long> versions = await store.VersionsAsync(cancellationToken).ConfigureAwait(false);
            SettingsSnapshot current = settings.Current;
            List<string> changed = [.. SettingsDefinitions.All
                .Where(definition => versions.GetValueOrDefault(definition.Name) > current[definition.Name].Version)
                .Select(definition => definition.Name)];

            if (changed.Count > 0)
            {
                settings.Publish(await store.LoadAsync(changed, cancellationToken).ConfigureAwait(false));
            }

            // Another host's apply shows on this process's pages too.
            List<SettingsHostState> rows = await database.SettingsHostStates.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
            Dictionary<string, string> signatures = rows
                .GroupBy(row => row.Section, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => string.Join(";", group.OrderBy(row => row.Host, StringComparer.Ordinal).Select(row => $"{row.Host},{row.AppliedVersion},{row.State},{row.Message}")),
                    StringComparer.Ordinal);

            foreach ((string section, string signature) in signatures)
            {
                if (_hostStates.TryGetValue(section, out string? seen) && seen != signature && !changed.Contains(section))
                {
                    changed.Add(section);
                }
            }

            _hostStates = signatures;

            foreach (string section in changed)
            {
                await views.PushAsync(section, cancellationToken).ConfigureAwait(false);
            }

            if (timeProvider.GetUtcNow() - _refreshed >= SettingsHostStates.RefreshInterval)
            {
                await hostStates.RefreshAsync(cancellationToken).ConfigureAwait(false);
                _refreshed = timeProvider.GetUtcNow();
            }
        }
        finally
        {
            _refresh.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(PollInterval, timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await RefreshAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogRefreshFailed(exception);
            }
        }
    }

    private async Task PushAsync(string section, CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopes.CreateScope();

        await scope.ServiceProvider.GetRequiredService<SettingsViews>().PushAsync(section, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 952, Level = LogLevel.Warning, Message = "Could not read the stored settings; the settings loaded before stay in force")]
    private partial void LogRefreshFailed(Exception exception);
}
