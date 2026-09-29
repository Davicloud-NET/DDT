// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Server.Data;
using DDT.Server.Endpoints;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// What an operator gives a waiting run on the machine's page: answers to its inputs, or a continue at its pause. A
// request that came too late gets the current run.
internal sealed class WaitingRunSaves(DdtDbContext database, RunQueries queries, WaitingRuns waiting, LiveNotifier live)
{
    public async Task<WaitingRunOutcome> AnswerAsync(Guid machineId, AnswerInputsRequest request, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == machineId, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            return WaitingRunOutcome.NotFound;
        }

        Deployment? run = await queries.ActiveAsync(machine, cancellationToken).ConfigureAwait(false);

        if (run is not { InputsPending: true })
        {
            return await AsItIsAsync(machine, cancellationToken).ConfigureAwait(false);
        }

        RunAnswering answering = await waiting
            .AnswerAsync(machine, run, new AnswersGiven(request.Answers, actor, AtMachine: false), cancellationToken)
            .ConfigureAwait(false);

        if (answering.Problems.Count > 0)
        {
            return new WaitingRunOutcome(null, false, DeploymentDecision.InvalidAnswers(answering.Problems));
        }

        if (answering.Refused || !await RunAnswerSaves.SaveAsync(database, run, answering.Before, cancellationToken).ConfigureAwait(false))
        {
            database.ChangeTracker.Clear();

            return await AsItIsAsync(machine, cancellationToken).ConfigureAwait(false);
        }

        return await ChangedAsync(machine, run, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WaitingRunOutcome> ContinueAsync(Guid machineId, ContinueRunRequest request, Actor actor, CancellationToken cancellationToken)
    {
        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == machineId, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            return WaitingRunOutcome.NotFound;
        }

        DeploymentDecision decision = await waiting.ContinueAsync(machine, request, actor, cancellationToken).ConfigureAwait(false);

        if (decision is not { Outcome: DeploymentOutcome.Accepted, Deployment: { } run })
        {
            return await AsItIsAsync(machine, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();

            return await AsItIsAsync(machine, cancellationToken).ConfigureAwait(false);
        }

        return await ChangedAsync(machine, run, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WaitingRunOutcome> ChangedAsync(Machine machine, Deployment run, CancellationToken cancellationToken)
    {
        live.MachineChanged(machine, run);

        return new WaitingRunOutcome(await DeploymentViews.ReadAsync(database, run.Id, cancellationToken).ConfigureAwait(false), true, null);
    }

    // The machine's run as it is now, for a request that came too late to change it.
    private async Task<WaitingRunOutcome> AsItIsAsync(Machine machine, CancellationToken cancellationToken)
    {
        Guid? shown = (await queries.ShownAsync(machine, cancellationToken).ConfigureAwait(false))?.Id;

        return shown is { } runId && await DeploymentViews.ReadAsync(database, runId, cancellationToken).ConfigureAwait(false) is { } view
            ? new WaitingRunOutcome(view, false, null)
            : WaitingRunOutcome.NotFound;
    }
}
