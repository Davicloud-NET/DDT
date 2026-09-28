// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Server.Data;
using DDT.Server.Endpoints;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Deployments;

// Saves answers given at the machine while its run waits for them. Answers that complete what the run lacks start it.
// If answers were given on the machine's page first, those win and these are refused.
internal sealed class AgentAnswerSaves(
    DdtDbContext database,
    WaitingRuns waiting,
    RunStarts starts,
    LiveNotifier live,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory)
{
    private const string AnsweredAlready =
        "The run waits for no answers: it started, or its inputs were answered on the machine's page first. Ask the server for the current run.";

    public async Task<AgentAnswering> AnswerAsync(
        Machine machine,
        Guid runId,
        IReadOnlyList<InputAnswer>? answers,
        string? address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        Deployment? run = machine.ActiveDeploymentId == runId
            ? await database.Deployments.FindAsync([runId], cancellationToken).ConfigureAwait(false)
            : null;

        if (run is null)
        {
            return new AgentAnswering(null, "This machine has no such run. Ask the server for the current run.", NoSuchRun: true);
        }

        DeploymentState before = run.State;
        RunAnswering answering = await waiting
            .AnswerAsync(machine, run, new AnswersGiven(answers, new Actor(machine.SignedInByUserId, machine.SignedInUserName, address), AtMachine: true), cancellationToken)
            .ConfigureAwait(false);

        if (answering.Refused)
        {
            return new AgentAnswering(null, AnsweredAlready);
        }

        // The problems are listed by input, and the machine asks again.
        if (answering.Problems.Count > 0 || answering.Check is not { } check)
        {
            return new AgentAnswering(
                new AgentAnswersResult(null, answering.Asked, [.. answering.Problems.Select(problem => new InputProblem(problem.Name, problem.Message.Text))]),
                null);
        }

        RunStart? start = check.Missing.Count == 0
            ? await starts.StartAsync(machine, run, address, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false)
            : null;

        if (!await RunAnswerSaves.SaveAsync(database, run, answering.Before, cancellationToken).ConfigureAwait(false))
        {
            return new AgentAnswering(null, AnsweredAlready);
        }

        DeploymentLog.Changed(loggerFactory.CreateLogger(typeof(AgentDeploymentEndpoints)), run, before);
        live.MachineChanged(machine, run);

        if (start?.Error is { } error)
        {
            return new AgentAnswering(null, error);
        }

        return new AgentAnswering(
            start is { InputsPending: null }
                ? new AgentAnswersResult(RunValues.Effective(run), [], [])
                : new AgentAnswersResult(null, start?.InputsPending ?? check.AskedAtMachine, []),
            null);
    }
}
