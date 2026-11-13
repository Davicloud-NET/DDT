// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Server.Data;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// The applications an Install applications step installs, as frozen with the run, for its agent in Windows while the
// step runs. Nothing in them is secret, so a read is not audited.
public sealed class RunApplications(DdtDbContext database, RunQueries queries)
{
    public async Task<(AgentStepApplications? Applications, string? Refusal)> ReadAsync(
        Machine machine,
        Guid runId,
        Guid stepId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        (RunningStep? running, string? refusal) = await queries.RunningStepAsync(machine, runId, stepId, cancellationToken).ConfigureAwait(false);

        if (running is null)
        {
            return (null, refusal);
        }

        if (running.Step is not InstallApplicationsStep)
        {
            return (null, "That step installs no applications.");
        }

        if (machine.AgentEnvironment != AgentEnvironment.Windows)
        {
            return (null, $"Applications install in Windows, and this agent registered from {PhaseRules.Name(machine.AgentEnvironment)}.");
        }

        string? planned = await database.DeploymentApplicationPlans
            .AsNoTracking()
            .Where(p => p.DeploymentId == runId && p.StepId == stepId)
            .Select(p => p.Applications)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return (new AgentStepApplications(RunApplicationPlans.Read(planned)), null);
    }
}
