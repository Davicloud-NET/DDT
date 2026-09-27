// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Settings;

// Whether this host applied the sections it rebuilds a component for: pxe, oidc and proxies. Kept in memory for the
// page this process serves, and written to ddt."SettingsHostStates" for the pages other processes serve. Only this
// process writes this host's rows.
public sealed partial class SettingsHostStates(IServiceScopeFactory scopes, TimeProvider timeProvider, ILogger<SettingsHostStates> logger)
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    // A host that has not refreshed its rows for this long is gone, and its rows with it.
    public static readonly TimeSpan Stale = TimeSpan.FromDays(1);

    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly Dictionary<string, SettingsHostState> _local = new(StringComparer.Ordinal);
    private readonly List<(string Section, long Version, TaskCompletionSource Done)> _waiters = [];

    public static string Host { get; } = HostName();

    // Set once the page's pushes are wired, so an apply is pushed like a save.
    public Func<string, CancellationToken, Task>? Changed { get; set; }

    public SettingsHostState? Local(string section)
    {
        lock (_lock)
        {
            return _local.GetValueOrDefault(section);
        }
    }

    public void Record(string section, long version, SettingsApplyResult state, string? message, string? detail = null)
    {
        SettingsHostState row = new()
        {
            Host = Host,
            Section = section,
            AppliedVersion = version,
            State = state,
            Message = StoredText.Bound(message, SettingsHostState.MaxMessageLength),
            Detail = detail,
            UpdatedUtc = timeProvider.GetUtcNow(),
        };

        lock (_lock)
        {
            _local[section] = row;

            foreach ((string Section, long Version, TaskCompletionSource Done) waiter in _waiters.Where(waiter => waiter.Section == section && waiter.Version <= version).ToList())
            {
                waiter.Done.TrySetResult();
                _waiters.Remove(waiter);
            }
        }

        _ = PersistAsync(row);
    }

    // For a save that answers with the state of its own host: a subsystem this process rebuilds is quick, so the answer
    // waits for it a moment rather than show Pending.
    public async Task WaitAsync(string section, long version, TimeSpan timeout, CancellationToken cancellationToken)
    {
        TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            if (_local.GetValueOrDefault(section)?.AppliedVersion >= version)
            {
                return;
            }

            _waiters.Add((section, version, done));
        }

        try
        {
            await Task.WhenAny(done.Task, Task.Delay(timeout, timeProvider, cancellationToken)).ConfigureAwait(false);
        }
        finally
        {
            lock (_lock)
            {
                _waiters.RemoveAll(waiter => waiter.Done == done);
            }
        }
    }

    // Keeps this host's rows from counting as gone, and removes those of hosts that have not been seen for a day.
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        List<SettingsHostState> local;

        lock (_lock)
        {
            local = [.. _local.Values];
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        foreach (SettingsHostState row in local)
        {
            row.UpdatedUtc = now;
            await PersistAsync(row).ConfigureAwait(false);
        }

        using IServiceScope scope = scopes.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        DateTimeOffset cutoff = now - Stale;

        // Compared in memory: SQLite cannot compare DateTimeOffset values in a query.
        List<SettingsHostState> stale = [.. (await database.SettingsHostStates.ToListAsync(cancellationToken).ConfigureAwait(false))
            .Where(row => row.UpdatedUtc < cutoff)];

        if (stale.Count > 0)
        {
            database.SettingsHostStates.RemoveRange(stale);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // An apply is audited once per host and version. A restart that applies the same version again only refreshes the row.
    private async Task PersistAsync(SettingsHostState row)
    {
        await _writes.WaitAsync().ConfigureAwait(false);

        try
        {
            using IServiceScope scope = scopes.CreateScope();
            DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
            SettingsHostState? stored = await database.SettingsHostStates.FindAsync([row.Host, row.Section]).ConfigureAwait(false);
            bool changed = stored is null
                || stored.AppliedVersion != row.AppliedVersion
                || stored.State != row.State
                || stored.Message != row.Message
                || stored.Detail != row.Detail;

            if (stored is null)
            {
                stored = new SettingsHostState { Host = row.Host, Section = row.Section };
                database.SettingsHostStates.Add(stored);
            }

            bool newVersion = stored.AppliedVersion != row.AppliedVersion || stored.State != row.State;
            stored.AppliedVersion = row.AppliedVersion;
            stored.State = row.State;
            stored.Message = row.Message;
            stored.Detail = row.Detail;
            stored.UpdatedUtc = row.UpdatedUtc;

            if (newVersion)
            {
                database.AuditEvents.Add(new AuditEvent
                {
                    OccurredUtc = row.UpdatedUtc,
                    Action = row.State == SettingsApplyResult.Applied ? AuditActions.SettingsApplied : AuditActions.SettingsApplyFailed,
                    ActorName = row.Host,
                    SubjectId = row.Section,
                    Detail = StoredText.Bound(
                        row.State == SettingsApplyResult.Applied
                            ? $"{row.Host} applied version {row.AppliedVersion} of {row.Section}."
                            : $"{row.Host} could not apply version {row.AppliedVersion} of {row.Section}: {row.Message}",
                        AuditEvent.MaxDetailLength),
                });
            }

            await database.SaveChangesAsync().ConfigureAwait(false);

            if (changed && Changed is { } push)
            {
                await push(row.Section, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            LogPersistFailed(row.Section, exception);
        }
        finally
        {
            _writes.Release();
        }
    }

    private static string HostName()
    {
        string name = Environment.MachineName;

        return name.Length <= SettingsHostState.MaxHostLength ? name : name[..SettingsHostState.MaxHostLength];
    }

    [LoggerMessage(EventId = 960, Level = LogLevel.Warning, Message = "Could not record how this host applied the {Section} settings")]
    private partial void LogPersistFailed(string section, Exception exception);
}
