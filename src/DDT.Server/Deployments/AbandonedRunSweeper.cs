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

// An agent continues its run only with the run token, and every registration and report hands out a new one. An agent
// silent for longer than that token lasts can never continue, and its run would stay running with its files locked in
// the library. A run that still reports is never touched: every report records that the machine was seen.
public sealed partial class AbandonedRunSweeper(
    IServiceScopeFactory scopes,
    LiveNotifier live,
    TimeProvider timeProvider,
    ILogger<AbandonedRunSweeper> logger) : BackgroundService
{
    private static readonly TimeSpan s_interval = TimeSpan.FromHours(1);

    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
    {
        // Last seen lags a contact by up to its resolution, and the run token handed out then lasts from that contact.
        DateTimeOffset cutoff = timeProvider.GetUtcNow() - MachineTokenLifetimes.Run - MachineLogLimits.LastSeenResolution;

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        DeploymentService deployments = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        // SQLite cannot compare DateTimeOffset, so the age is judged here.
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
            if (await FailAsync(database, deployments, machineId, cutoff, cancellationToken).ConfigureAwait(false))
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

    // Nothing the agent holds is valid any more, so only a registration, a stop or a rejection can change the run in
    // between, and the concurrency tokens on the machine catch each of them.
    private async Task<bool> FailAsync(
        DdtDbContext database,
        DeploymentService deployments,
        Guid machineId,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();

        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == machineId, cancellationToken).ConfigureAwait(false);
        Deployment? run = machine is null ? null : await deployments.ActiveAsync(machine, cancellationToken).ConfigureAwait(false);

        if (machine is null || machine.LastSeenUtc >= cutoff || run is not { State: DeploymentState.Running })
        {
            return false;
        }

        await deployments.EndForLostContactAsync(machine, run, cancellationToken).ConfigureAwait(false);
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
