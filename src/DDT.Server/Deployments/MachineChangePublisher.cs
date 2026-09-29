// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Deployments;

// Saves a change to a machine and its run, then logs and pushes it, so neither shows a change that lost a race. Nothing
// is logged or pushed after a concurrency conflict, and the log keeps the caller's category.
public sealed class MachineChangePublisher(DdtDbContext database, RunQueries queries, LiveNotifier live, ILoggerFactory loggerFactory)
{
    // False on a concurrency conflict.
    public async Task<bool> SaveAsync(
        Machine machine,
        Deployment run,
        DeploymentState? before,
        Type logCategory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(logCategory);

        IReadOnlyList<DeploymentStep> changedSteps = RunReports.ChangedSteps(database);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }

        DeploymentLog.Changed(loggerFactory.CreateLogger(logCategory), run, before);
        live.MachineChanged(machine, run);
        live.RunStepsChanged(machine.Id, changedSteps);

        return true;
    }

    // A decision about the machine that may have ended its active run. Shown is the run the machine's row shows now.
    public async Task<(bool Saved, Deployment? Shown)> SaveDecisionAsync(
        Machine machine,
        Deployment? active,
        DeploymentState? before,
        Type logCategory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(logCategory);

        IReadOnlyList<DeploymentStep> changedSteps = RunReports.ChangedSteps(database);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (false, null);
        }

        if (active is not null)
        {
            DeploymentLog.Changed(loggerFactory.CreateLogger(logCategory), active, before);
        }

        live.RunStepsChanged(machine.Id, changedSteps);

        Deployment? shown = await queries.ShownAsync(machine, cancellationToken).ConfigureAwait(false);
        live.MachineChanged(machine, shown);

        return (true, shown);
    }
}
