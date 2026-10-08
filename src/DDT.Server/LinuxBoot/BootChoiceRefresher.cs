// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Images;
using DDT.Core.Boot;
using DDT.Server.Configuration;
using DDT.Server.Data;
using DDT.Server.Deployments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DDT.Server.LinuxBoot;

// Fills BootChoices from the database: every few seconds, which also covers a pxe host that is another process than
// the one a machine changed in, and at once when what netboot reads of a machine changes in this one. A read that
// fails leaves the list as it was.
public sealed partial class BootChoiceRefresher(
    BootChoices choices,
    LinuxBootEnvironment linuxBoot,
    IServiceScopeFactory scopes,
    IOptions<DdtOptions> options,
    TimeProvider timeProvider,
    ILogger<BootChoiceRefresher> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    // A rollout changes many machines in the same moment. One read a second serves them all.
    private static readonly TimeSpan s_pause = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Only netboot asks the list.
        if (!DeploymentRoles.Parse(options.Value.Roles).Contains(DeploymentRole.Pxe))
        {
            return;
        }

        bool failing = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            Task changed = choices.NextChange();

            // The database may be down or slow. Netboot goes on with what it knew, and the next turn tries again.
            try
            {
                await RefreshAsync(stoppingToken).ConfigureAwait(false);

                if (failing)
                {
                    LogReadAgain();
                    failing = false;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                if (!failing)
                {
                    LogReadFailed(exception);
                    failing = true;
                }
            }

            try
            {
                await Task.Delay(s_pause, timeProvider, stoppingToken).ConfigureAwait(false);
                await changed.WaitAsync(Interval - s_pause, timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Nothing changed here. Another process may have changed a machine.
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    // Reads of every machine what netboot asks, and of every active run where it starts, and replaces the list in one
    // step. Nothing else of a row is read, since a run's row holds its answers, values and variables.
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        RunQueries queries = scope.ServiceProvider.GetRequiredService<RunQueries>();
        UefiCa? shimSignedUnder = linuxBoot.Current.ShimSignedUnder;

        var machines = await database.Machines
            .AsNoTracking()
            .Select(machine => new
            {
                machine.SmbiosUuid,
                machine.MacAddresses,
                machine.SecureBootEnabled,
                machine.TrustedUefiCas,
                machine.ActiveDeploymentId,
                machine.NextEnvironment,
                machine.NextEnvironmentUtc,
                Approved = machine.FirstApprovedUtc != null,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var runs = await database.Deployments
            .AsNoTracking()
            .Where(run => database.Machines.Any(machine => machine.ActiveDeploymentId == run.Id))
            .Select(run => new { run.Id, run.BootEnvironment })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        Dictionary<Guid, AgentEnvironment> starts = [];

        // A run from before the server kept its boot environment says it through its sequence.
        foreach (var run in runs)
        {
            starts[run.Id] = run.BootEnvironment ?? await queries.StartsInAsync(run.Id, cancellationToken).ConfigureAwait(false);
        }

        choices.Replace(machines.Select(machine => new BootChoiceEntry(
            Guid.TryParse(machine.SmbiosUuid, out Guid uuid) ? uuid : null,
            machine.MacAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Run = machine.ActiveDeploymentId is { } active && starts.TryGetValue(active, out AgentEnvironment environment) ? environment : null,
            Next = machine.NextEnvironment,
            NextUtc = machine.NextEnvironmentUtc,
            LinuxRefused = machine.SecureBootEnabled == true && MicrosoftUefiCa.Untrusted(machine.TrustedUefiCas, shimSignedUnder),
            Approved = machine.Approved,
        }));
    }

    [LoggerMessage(
        EventId = 986,
        Level = LogLevel.Warning,
        Message = "Could not read which boot environment each machine starts. Netboot goes on with what it knew, and with the default for the rest")]
    private partial void LogReadFailed(Exception exception);

    [LoggerMessage(EventId = 987, Level = LogLevel.Information, Message = "Reading which boot environment each machine starts works again")]
    private partial void LogReadAgain();
}
