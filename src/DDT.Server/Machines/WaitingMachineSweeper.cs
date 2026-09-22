// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;
using DDT.Server.Data;
using DDT.Server.Live;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Machines;

// Anyone who reaches the server can register a machine, so registrations nobody ever approved are removed once
// they have not been seen for a while. A machine approved once is never removed here: it keeps its log.
public sealed partial class WaitingMachineSweeper(
    IServiceScopeFactory scopes,
    LiveNotifier live,
    TimeProvider timeProvider,
    ILogger<WaitingMachineSweeper> logger) : BackgroundService
{
    private static readonly TimeSpan s_interval = TimeSpan.FromHours(1);

    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset cutoff = timeProvider.GetUtcNow() - MachineLogLimits.WaitingMachineLifetime;

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();

        // SQLite cannot compare DateTimeOffset, so the age is judged here. The delete repeats the rest of the
        // condition, because a machine may have been approved in between. A machine an operator assigned a sequence
        // is kept: it waits for its next netboot or a sign-in at it.
        var waiting = await database.Machines
            .Where(m => m.State == MachineState.Pending && m.FirstApprovedUtc == null && m.ActiveDeploymentId == null)
            .Select(m => new { m.Id, m.LastSeenUtc })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Guid[] stale = [.. waiting.Where(m => m.LastSeenUtc < cutoff).Select(m => m.Id)];

        if (stale.Length == 0)
        {
            return 0;
        }

        int removed = await database.Machines
            .Where(m => stale.Contains(m.Id)
                && m.State == MachineState.Pending
                && m.FirstApprovedUtc == null
                && m.ActiveDeploymentId == null)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (removed > 0)
        {
            LogSwept(removed);
            live.MachinesRemoved();
        }

        return removed;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(s_interval, timeProvider);

        do
        {
            // A failed pass must not stop the host, which also runs the web UI and the pxe role.
            try
            {
                await SweepOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogSweepFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(EventId = 420, Level = LogLevel.Information, Message = "Removed {Count} machines nobody approved that were not seen for a day")]
    private partial void LogSwept(int count);

    [LoggerMessage(EventId = 421, Level = LogLevel.Warning, Message = "Could not remove stale machines nobody approved")]
    private partial void LogSweepFailed(Exception exception);
}
