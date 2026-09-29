// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Server.Data;
using DDT.Server.Endpoints;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Deployments;

// Saves an agent's report, then logs and pushes it. A running run does not poll, so every report refreshes last seen and
// hands out the tokens a poll would.
internal sealed class RunReportSaves(
    DdtDbContext database,
    RunReports reports,
    MachineTokenService tokens,
    LiveNotifier live,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory)
{
    private const int MaxAttempts = 3;

    // How soon the agent reports again while its run waits for someone to answer or to continue it.
    private const int WaitingReportSeconds = 5;

    public async Task<ReportOutcome> SaveAsync(IncomingReport reported, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reported);

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await TrySaveAsync(reported, cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // Decide again from what's stored now. If a stop or a new registration bumped the generation, the next
                // attempt answers 401.
                database.ChangeTracker.Clear();
            }
        }
    }

    private async Task<ReportOutcome> TrySaveAsync(IncomingReport reported, CancellationToken cancellationToken)
    {
        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == reported.MachineId, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            return ReportOutcome.NotFound;
        }

        // If the machine was stopped, rejected or registered again after the token was checked, the run is over for
        // this agent.
        if (!Principals.HoldsCurrentGeneration(reported.User, machine))
        {
            return new ReportOutcome(null, null, Unauthorized: true);
        }

        string? address = reported.Address;
        DeploymentState? before = (await database.Deployments.FindAsync([reported.RunId], cancellationToken).ConfigureAwait(false))?.State;
        DeploymentDecision decision = await reports.ApplyAsync(machine, reported.RunId, reported.Report, address, cancellationToken).ConfigureAwait(false);

        if (decision.Outcome == DeploymentOutcome.Unchanged && decision.Deployment is { } unchanged)
        {
            return new ReportOutcome(Tokens(machine, unchanged), null);
        }

        if (decision is not { Outcome: DeploymentOutcome.Accepted or DeploymentOutcome.Refused, Deployment: { } run })
        {
            return new ReportOutcome(null, decision);
        }

        IReadOnlyList<DeploymentStep> changedSteps = RunReports.ChangedSteps(database);
        bool variablesChanged = RunReports.VariablesChanged(database, run);
        LastSeen.Record(machine, timeProvider.GetUtcNow(), address);

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        DeploymentLog.Changed(loggerFactory.CreateLogger(typeof(AgentDeploymentEndpoints)), run, before);
        live.MachineChanged(machine, run);
        live.RunStepsChanged(machine.Id, changedSteps);

        if (variablesChanged && RunVariables.Read(run.Variables) is { } variables)
        {
            live.RunVariablesChanged(machine.Id, run.Id, variables);
        }

        return decision.Outcome == DeploymentOutcome.Refused
            ? new ReportOutcome(null, decision)
            : new ReportOutcome(Answer(Tokens(machine, run), run, reported.Report, decision), null);
    }

    // The run token comes with every answer while the run runs, so the agent has one on disk before it first restarts.
    private AgentRunReportResult Tokens(Machine machine, Deployment run) =>
        new(
            tokens.IssueCurrent(machine),
            tokens.Issue(machine, MachineTokenPurpose.Resume),
            run.State == DeploymentState.Running ? tokens.IssueRunToken(machine, run.Id) : null);

    // The values go with the report that started the run, and with every report before the agent has begun, because the
    // answer that started it can get lost. A pause someone continued is sent with every answer until its visit is over.
    private static AgentRunReportResult Answer(AgentRunReportResult tokens, Deployment run, AgentRunReport report, DeploymentDecision decision) =>
        tokens with
        {
            Values = run.State == DeploymentState.Running && (decision.Started || report.Activity is RunActivity.Preparing or RunActivity.WaitingForInput)
                ? RunValues.Effective(run)
                : null,
            InputsPending = run.InputsPending ? decision.InputsPending ?? [] : null,
            ContinueStepId = run.ContinueStepId,
            ContinuePass = run.ContinueStepId is null ? null : run.ContinuePass,
            ReportAfterSeconds = DeploymentSummaries.Waiting(run) ? WaitingReportSeconds : null,
        };
}
