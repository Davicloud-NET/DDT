// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Messages;
using DDT.Server.Data;
using DDT.Server.Endpoints;
using DDT.Server.Images;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// A run assigned on the Machines page, or the machine's run stopped there, saved, then logged and pushed.
internal sealed class MachineRuns(
    DdtDbContext database,
    RunQueries queries,
    RunAssignments assignments,
    RunTermination termination,
    ImageStore store,
    MachineChangePublisher publisher)
{
    public async Task<MachineOutcome> AssignAsync(Guid id, AssignSequenceRequest request, Actor actor, CancellationToken cancellationToken)
    {
        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            return MachineOutcome.NotFound;
        }

        // Under the library lock, so nothing the run downloads can be deleted between its lookup and the save.
        await using SemaphoreHold hold = await store.LibraryLock.EnterAsync(cancellationToken).ConfigureAwait(false);

        DeploymentDecision decision = await assignments.AssignAsync(machine, request, actor, cancellationToken).ConfigureAwait(false);

        return await CompleteAsync(decision, machine, null, ServerMessages.MachineChangedWhileAssigning.With(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<MachineOutcome> EndCurrentAsync(Guid id, Actor actor, CancellationToken cancellationToken)
    {
        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            return MachineOutcome.NotFound;
        }

        DeploymentState? before = (await queries.ActiveAsync(machine, cancellationToken).ConfigureAwait(false))?.State;
        DeploymentDecision decision = await termination.EndCurrentAsync(machine, actor, cancellationToken).ConfigureAwait(false);

        return await CompleteAsync(decision, machine, before, ServerMessages.MachineRunChangedWhileStopping.With(), cancellationToken).ConfigureAwait(false);
    }

    // Conflict is the refusal when the machine changed before the save.
    private async Task<MachineOutcome> CompleteAsync(
        DeploymentDecision decision,
        Machine machine,
        DeploymentState? before,
        ServerMessage conflict,
        CancellationToken cancellationToken)
    {
        if (decision is not { Outcome: DeploymentOutcome.Accepted, Deployment: { } run })
        {
            return MachineOutcome.Refused(decision);
        }

        return await publisher.SaveAsync(machine, run, before, typeof(MachineEndpoints), cancellationToken).ConfigureAwait(false)
            ? MachineOutcome.Saved(MachineSummaries.From(machine, run))
            : MachineOutcome.Refused(DeploymentDecision.Conflict(conflict));
    }
}
