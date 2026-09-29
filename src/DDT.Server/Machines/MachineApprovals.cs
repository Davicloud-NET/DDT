// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Messages;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Endpoints;
using DDT.Server.Images;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Machines;

// An approval on the Machines page. If the request names the sequence the page showed a rule choosing, the approval
// runs it too.
internal sealed class MachineApprovals(
    DdtDbContext database,
    MachineTransitions transitions,
    RunAssignments assignments,
    ImageStore store,
    MachineChangePublisher publisher,
    DdtSettings settings)
{
    public async Task<MachineOutcome> ApproveAsync(
        Guid id,
        ApproveMachineRequest? request,
        Actor actor,
        CancellationToken cancellationToken)
    {
        bool requireWebApproval = settings.Current.Machines.RequireWebApproval;

        ServerMessage? Refusal(Machine machine) => machine.State != MachineState.Pending
            ? ServerMessages.MachineInState.With("state", MachineTransitions.StateName(machine.State))
            : requireWebApproval && machine.SignedInByUserId is null
                ? ServerMessages.MachineNobodySignedIn.With()
                : null;

        if (request?.ExpectedSequenceId is null)
        {
            return await transitions.ApproveAsync(id, Refusal, actor, cancellationToken).ConfigureAwait(false);
        }

        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            return MachineOutcome.NotFound;
        }

        if (Refusal(machine) is { } reason)
        {
            return MachineOutcome.Refused(DeploymentDecision.Conflict(reason));
        }

        // Under the library lock, so nothing the run downloads can be deleted between its lookup and the save.
        await using SemaphoreHold hold = await store.LibraryLock.EnterAsync(cancellationToken).ConfigureAwait(false);

        DeploymentDecision decision = await assignments.ApproveByRuleAsync(machine, request, actor, cancellationToken).ConfigureAwait(false);

        if (decision is not { Outcome: DeploymentOutcome.Accepted, Deployment: { } run })
        {
            return MachineOutcome.Refused(decision);
        }

        return await publisher.SaveAsync(machine, run, null, typeof(MachineEndpoints), cancellationToken).ConfigureAwait(false)
            ? MachineOutcome.Saved(MachineSummaries.From(machine, run))
            : MachineOutcome.Refused(DeploymentDecision.Conflict(ServerMessages.MachineChangedWhileDeciding.With()));
    }
}
