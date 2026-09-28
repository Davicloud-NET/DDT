// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Live;
using DDT.Server.Rules;
using Microsoft.Extensions.Logging;
using DeploymentStep = DDT.Server.Deployments.DeploymentStep;

namespace DDT.Server.Machines;

// Saves a registration, then logs and pushes it, so neither shows a change that lost a race. A concurrency conflict is
// left to MachineRegistrar, which decides again.
public sealed class RegistrationPublisher(
    DdtDbContext database,
    RunQueries queries,
    LiveNotifier live,
    RuleRecount recount,
    ILogger<MachineRegistrar> logger)
{
    public async Task PublishAsync(Machine machine, Arrival arrival, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(arrival);

        IReadOnlyList<DeploymentStep> changedSteps = RunReports.ChangedSteps(database);

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (arrival.Active is not null)
        {
            DeploymentLog.Changed(logger, arrival.Active, arrival.Before);
        }

        live.RunStepsChanged(machine.Id, changedSteps);
        live.MachineChanged(machine, await queries.ShownAsync(machine, cancellationToken).ConfigureAwait(false));

        // A rule counts the machines it matches, which a new machine, or one that reports otherwise now, may change.
        if (arrival.Tested != RuleRecount.Tested(machine))
        {
            recount.MachinesChanged();
        }
    }
}
