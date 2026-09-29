// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Deployments;

// Fails a run whose agent has been silent for longer than a run token lasts. That run can never continue, and it would
// keep its files locked in the library. Every report records the machine as seen, so a run that reports is never
// touched.
public sealed partial class AbandonedRunSweeper(
    IServiceScopeFactory scopes,
    LiveNotifier live,
    TimeProvider timeProvider,
    ILogger<AbandonedRunSweeper> logger) : BackgroundService
{
    private static readonly TimeSpan s_interval = TimeSpan.FromHours(1);

    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
    {
        // Last seen can lag a contact by up to LastSeenResolution, and the run token handed out at that contact lasts
        // from then.
        DateTimeOffset cutoff = timeProvider.GetUtcNow() - MachineTokenLifetimes.Run - MachineLogLimits.LastSeenResolution;

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();

        // SQLite can't compare DateTimeOffset, so the age is checked here.
        var running = await (
                from machine in database.Machines
                join run in database.Deployments on machine.ActiveDeploymentId equals (Guid?)run.Id
                where run.State == DeploymentState.Running
                select new { machine.Id, machine.LastSeenUtc })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int failed = 0;

        foreach (Guid machineId in running.Where(m => m.LastSeenUtc < cutoff).Select(m => m.Id))
        {
            if (await FailAsync(scope.ServiceProvider, machineId, cutoff, cancellationToken).ConfigureAwait(false))
            {
                failed++;
            }
        }

        if (failed > 0)
        {
            LogSwept(failed);
        }

        return failed;
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

    // Nothing the agent holds is valid any more. So only a registration, a stop or a rejection can change the run in
    // between, and the machine's concurrency tokens catch each of them.
    private async Task<bool> FailAsync(IServiceProvider services, Guid machineId, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        DdtDbContext database = services.GetRequiredService<DdtDbContext>();
        database.ChangeTracker.Clear();

        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == machineId, cancellationToken).ConfigureAwait(false);
        Deployment? run = machine is null
            ? null
            : await services.GetRequiredService<RunQueries>().ActiveAsync(machine, cancellationToken).ConfigureAwait(false);

        if (machine is null || machine.LastSeenUtc >= cutoff || run is not { State: DeploymentState.Running })
        {
            return false;
        }

        await services.GetRequiredService<RunTermination>().EndForLostContactAsync(machine, run, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DeploymentStep> changedSteps = RunReports.ChangedSteps(database);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }

        DeploymentLog.Changed(logger, run, DeploymentState.Running);
        live.MachineChanged(machine, run);
        live.RunStepsChanged(machine.Id, changedSteps);

        return true;
    }

    [LoggerMessage(EventId = 956, Level = LogLevel.Warning, Message = "Failed {Count} runs whose agent was out of contact for longer than a run token lasts")]
    private partial void LogSwept(int count);

    [LoggerMessage(EventId = 957, Level = LogLevel.Warning, Message = "Could not fail runs whose agent was out of contact")]
    private partial void LogSweepFailed(Exception exception);
}
