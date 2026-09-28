// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Server.Data;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// Ends a machine's run from outside the run: a stop, a rejection, a restart of the machine or an agent gone silent.
// Nothing here saves. The caller saves the change with its audit rows, and Machine's concurrency tokens settle the
// races.
public sealed class RunTermination(DdtDbContext database, RunQueries queries, TimeProvider timeProvider)
{
    private const string SomeOperator = "an operator";

    // Cancels an assigned run, or stops a running one. The agent's next call is refused, and its resume token no longer
    // matches. So the machine starts over as Pending when it registers again.
    public async Task<DeploymentDecision> EndCurrentAsync(Machine machine, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        Deployment? active = await queries.ActiveAsync(machine, cancellationToken).ConfigureAwait(false);
        string by = actor.Name ?? SomeOperator;

        switch (active?.State)
        {
            case DeploymentState.Assigned:
                Cancel(machine, active, actor, $"Cancelled by {by}.");

                return DeploymentDecision.Accepted(active);

            case DeploymentState.Running:
                await FailAsync(machine, active, actor, $"Stopped by {by}.", cancellationToken).ConfigureAwait(false);
                machine.State = MachineState.Failed;
                machine.TokenGeneration++;

                return DeploymentDecision.Accepted(active);

            default:
                return DeploymentDecision.Conflict(ServerMessages.DeploymentNothingToEnd.With());
        }
    }

    // The caller rejects the machine itself, so the machine's state is left alone here.
    public async Task EndForRejectionAsync(
        Machine machine,
        Deployment? active,
        Actor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(actor);

        string by = actor.Name ?? SomeOperator;

        switch (active?.State)
        {
            case DeploymentState.Assigned:
                Cancel(machine, active, actor, $"The machine was rejected by {by}.");
                break;

            case DeploymentState.Running:
                await FailAsync(machine, active, actor, $"Rejected by {by}.", cancellationToken).ConfigureAwait(false);
                break;
        }

        machine.ActiveDeploymentId = null;
    }

    // A registration that doesn't continue the machine's run means the agent that had it is gone, so a running run
    // fails. A run chosen at the machine or by a rule is cancelled, because the disk and the ERASE typed there, and the
    // approval that took the rule's sequence, belonged to that boot. A web assignment stays for the next sign-in or
    // netboot.
    public async Task EndForRestartAsync(
        Machine machine,
        Deployment? active,
        string? address,
        bool presentedRunToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        switch (active)
        {
            case { State: DeploymentState.Running }:
                string error = presentedRunToken
                    ? "The machine started again during the run, with a run token the server no longer accepts."
                    : "The machine started again during the run, without the run's token, so the run could not continue.";
                await FailAsync(machine, active, Actor.OfMachine(machine.Id, address), error, cancellationToken).ConfigureAwait(false);
                break;

            case { State: DeploymentState.Assigned, Source: DeploymentSource.Console or DeploymentSource.Rule }:
                Cancel(machine, active, Actor.OfMachine(machine.Id, address), "The machine started again before the run began.");
                break;
        }
    }

    // For a running run whose agent has been silent for longer than a run token lasts (see AbandonedRunSweeper).
    public async Task EndForLostContactAsync(Machine machine, Deployment running, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(running);

        string error = $"The agent has not been in contact since {machine.LastSeenUtc:u} and could no longer resume the run.";

        await FailAsync(machine, running, Actor.Nobody, error, cancellationToken).ConfigureAwait(false);
        machine.State = MachineState.Failed;
        machine.TokenGeneration++;
    }

    // A run that ends is no longer the machine's active one.
    internal static void End(Machine machine, Deployment run, DeploymentState state, string? error, DateTimeOffset now)
    {
        run.State = state;
        run.Error = error;
        run.FinishedUtc = now;
        run.UpdatedUtc = now;
        machine.ActiveDeploymentId = null;
    }

    private void Cancel(Machine machine, Deployment run, Actor actor, string reason)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        End(machine, run, DeploymentState.Cancelled, null, now);
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.DeploymentCancelled,
            run.Id.ToString("D"),
            actor,
            now,
            $"{run.Title} on machine {machine.Id:D}. {reason}"));
    }

    // The step that runs fails with the run.
    private async Task FailAsync(Machine machine, Deployment run, Actor actor, string error, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<DeploymentStep> running = await database.DeploymentSteps
            .Where(s => s.DeploymentId == run.Id && s.State == StepState.Running)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        RunReports.FailRunning(running, error, now);
        End(machine, run, DeploymentState.Failed, error, now);
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.DeploymentFailed,
            run.Id.ToString("D"),
            actor,
            now,
            $"{run.Title} on machine {machine.Id:D}. {error}"));
    }
}
