// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Messages;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Machines;

// An approval or a rejection on the Machines page, saved with its audit row, then logged and pushed.
internal sealed class MachineTransitions(
    DdtDbContext database,
    RunQueries queries,
    RunTermination termination,
    MachineChangePublisher publisher,
    TimeProvider timeProvider)
{
    // Refusal says why the machine cannot be approved in its state, null when it can.
    public Task<MachineOutcome> ApproveAsync(Guid id, Func<Machine, ServerMessage?> refusal, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        return ApplyAsync(
            id,
            new Transition(AuditActions.MachineApproved, refusal, (machine, _, now) =>
            {
                machine.Approve(actor.UserId, now);

                return Task.CompletedTask;
            }),
            actor,
            cancellationToken);
    }

    public Task<MachineOutcome> RejectAsync(Guid id, Actor actor, CancellationToken cancellationToken) =>
        ApplyAsync(
            id,
            new Transition(
                AuditActions.MachineRejected,
                machine => machine.State is MachineState.Pending or MachineState.Approved or MachineState.Deploying or MachineState.Failed
                    ? null
                    : ServerMessages.MachineInState.With("state", StateName(machine.State)),
                (machine, active, _) =>
                {
                    // The generation bump kills every token already issued, so a rejected machine stops mid request
                    // rather than at its next token refresh, a running deployment included.
                    machine.State = MachineState.Rejected;
                    machine.TokenGeneration++;
                    machine.ApprovedByUserId = null;
                    machine.ApprovedUtc = null;

                    return termination.EndForRejectionAsync(machine, active, actor, cancellationToken);
                }),
            actor,
            cancellationToken);

    public static ServerMessage StateName(MachineState state) => (state switch
    {
        MachineState.Pending => ServerMessages.MachineStatePending,
        MachineState.Approved => ServerMessages.MachineStateApproved,
        MachineState.Deploying => ServerMessages.MachineStateDeploying,
        MachineState.Done => ServerMessages.MachineStateDone,
        MachineState.Failed => ServerMessages.MachineStateFailed,
        MachineState.Rejected => ServerMessages.MachineStateRejected,
        MachineState.Retired => ServerMessages.MachineStateRetired,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    }).With();

    private async Task<MachineOutcome> ApplyAsync(Guid id, Transition transition, Actor actor, CancellationToken cancellationToken)
    {
        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            return MachineOutcome.NotFound;
        }

        if (transition.Refusal(machine) is { } reason)
        {
            return MachineOutcome.Refused(DeploymentDecision.Conflict(reason));
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        MachineState previous = machine.State;
        Deployment? active = await queries.ActiveAsync(machine, cancellationToken).ConfigureAwait(false);
        DeploymentState? activeState = active?.State;

        await transition.Apply(machine, active, now).ConfigureAwait(false);

        database.AuditEvents.Add(AuditEvents.Create(
            transition.Action,
            machine.Id.ToString("D"),
            actor,
            now,
            machine.SignedInUserName is { } signer
                ? $"Was {previous}. Signed in at the machine by {signer}."
                : $"Was {previous}."));

        (bool saved, Deployment? shown) = await publisher
            .SaveDecisionAsync(machine, active, activeState, typeof(MachineEndpoints), cancellationToken)
            .ConfigureAwait(false);

        return saved
            ? MachineOutcome.Saved(MachineSummaries.From(machine, shown))
            : MachineOutcome.Refused(DeploymentDecision.Conflict(ServerMessages.MachineChangedWhileDeciding.With()));
    }

    // Action is the audit's; Apply changes the machine and its active run, given the time of the change.
    private sealed record Transition(string Action, Func<Machine, ServerMessage?> Refusal, Func<Machine, Deployment?, DateTimeOffset, Task> Apply);
}
