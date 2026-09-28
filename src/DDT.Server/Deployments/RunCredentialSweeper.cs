// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Deployments;

// RunCredentialCleanup deletes a run's credentials in the save that ends it. A run that ended without a save through
// this build, such as by a change made to the database by hand or by another build, would keep them, so each start
// removes the credentials of every run that is no longer assigned or running. It runs once the schema is up to date.
public sealed partial class RunCredentialSweeper(
    IServiceScopeFactory scopes,
    ILogger<RunCredentialSweeper> logger) : IHostedService
{
    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();

        return await database.RunCredentials
            .Where(c => database.Deployments.Any(d => d.Id == c.DeploymentId
                && d.State != DeploymentState.Assigned
                && d.State != DeploymentState.Running))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // A failed sweep must not stop the host, which also runs the web UI and the pxe role; the next start tries again.
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            int removed = await SweepOnceAsync(cancellationToken).ConfigureAwait(false);

            if (removed > 0)
            {
                LogSwept(removed);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogSweepFailed(exception);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 958, Level = LogLevel.Warning, Message = "Removed {Count} credentials given for runs that were over")]
    private partial void LogSwept(int count);

    [LoggerMessage(EventId = 959, Level = LogLevel.Warning, Message = "Could not remove the credentials given for runs that are over")]
    private partial void LogSweepFailed(Exception exception);
}
