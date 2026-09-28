// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Server.Endpoints;
using DDT.Server.Images;
using DDT.Server.Machines;

namespace DDT.Server.Deployments;

// Saves the run of a sequence picked at the machine, and hands it to the agent.
internal sealed class SequencePickSaves(SequencePicks picks, RunQueries queries, ImageStore store, MachineChangePublisher publisher)
{
    // Run is null if nothing was saved, and Decision says why.
    public async Task<(AgentRun? Run, DeploymentDecision Decision)> PickAsync(
        Machine machine,
        AgentRunRequest request,
        string? address,
        CancellationToken cancellationToken)
    {
        Deployment run;

        // Under the library lock, so nothing the run downloads can be deleted between its lookup and the save.
        await using (await store.LibraryLock.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            DeploymentDecision decision = await picks.PickAsync(machine, request, address, cancellationToken).ConfigureAwait(false);

            if (decision is not { Outcome: DeploymentOutcome.Accepted, Deployment: { } picked })
            {
                return (null, decision);
            }

            if (!await publisher.SaveAsync(machine, picked, null, typeof(AgentDeploymentEndpoints), cancellationToken).ConfigureAwait(false))
            {
                return (null, DeploymentDecision.Conflict("The machine changed while the sequence was chosen. Choose the sequence again."));
            }

            run = picked;
        }

        return (await queries.AgentRunAsync(machine, run, cancellationToken).ConfigureAwait(false), DeploymentDecision.Accepted(run));
    }
}
