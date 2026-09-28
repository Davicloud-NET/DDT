// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;

namespace DDT.Agent;

// Acts on the run a poll names: runs it, goes on with the one on the disk, or reports as failed a run nothing here can
// go on with.
internal sealed class RunStarter(
    IAgentServer server,
    SequenceRunner runner,
    LocalRunTracker runs,
    ConsolePrompts prompts,
    AgentRegistrar registrar,
    AgentLog log)
{
    // A run that ended in this process without the server hearing so, and the Failed report that tells it.
    private (Guid RunId, AgentRunReport Report)? _abandonedRun;

    // The exit code for a run that ended the agent's part; null for one that failed, or lost the machine's token.
    public static int? ExitCodeAfter(RunOutcome outcome) => outcome switch
    {
        RunOutcome.Finished => AgentExitCodes.Deployed,
        RunOutcome.Restarting => AgentExitCodes.Restarting,
        RunOutcome.Stopped => AgentExitCodes.Stopped,
        _ => null,
    };

    // The outcome of the run it ran, or null when it ran none. tokens are the poll's, which every answer renews.
    public async Task<RunOutcome?> HandleAsync(Guid machineId, DeploymentTokens tokens, AgentRun? run, CancellationToken cancellationToken)
    {
        if (run is null)
        {
            return null;
        }

        LocalRun? resumable = runs.ResumableFor(run);

        if (run.State == DeploymentState.Assigned && run.Id == prompts.PickedWithoutErase && SequenceTree.Nodes(run.Sequence).Any(step => step.ErasesDisk))
        {
            // The picker said why when the server answered the choice.
            await ReportAsync(machineId, tokens, run.Id, FailedRunReport.Of(SequencePhase.WindowsPE, SequencePicker.ChangedAfterChoiceMessage), cancellationToken).ConfigureAwait(false);
            prompts.PickedWithoutErase = null;
        }
        else if (run.State == DeploymentState.Assigned || resumable is not null)
        {
            return await RunAsync(machineId, tokens, run, resumable, cancellationToken).ConfigureAwait(false);
        }
        else if (run.State == DeploymentState.Running)
        {
            // Nothing here can go on with it: its state is not on this machine's disks, a refused token
            // ended it, or its own failure report did not get through.
            AgentRunReport report = _abandonedRun is { } abandoned && abandoned.RunId == run.Id
                ? abandoned.Report
                : FailedRunReport.Of(SequencePhase.WindowsPE, SequenceRunner.LostContactMessage);

            log.Error(report.Error!);
            await ReportAsync(machineId, tokens, run.Id, report, cancellationToken).ConfigureAwait(false);
            _abandonedRun = null;
            runs.RunToken = null;
        }

        return null;
    }

    private async Task<RunOutcome> RunAsync(Guid machineId, DeploymentTokens pollTokens, AgentRun run, LocalRun? resumable, CancellationToken cancellationToken)
    {
        runs.Forget();
        DeploymentTokens tokens = new(pollTokens.Token, pollTokens.ResumeToken, runs.RunToken);
        RunResult result = await runner
            .RunAsync(new RunRequest(machineId, run, resumable, resumable is null ? prompts.ConfirmedDisk : null, tokens, registrar.LastIdentity!), cancellationToken)
            .ConfigureAwait(false);

        _abandonedRun = result.UnsentReport is { } unsent ? (run.Id, unsent) : null;

        // The run kept the session alive; the tokens the poll last saw may have expired.
        pollTokens.Update(tokens.Token, tokens.ResumeToken);
        runs.RunToken = tokens.RunToken;

        // A run that failed leaves the machine to pick again.
        if (ExitCodeAfter(result.Outcome) is null && result.Outcome != RunOutcome.TokenRejected)
        {
            runs.RunToken = null;
            prompts.ResetPicker();
        }

        return result.Outcome;
    }

    private async Task ReportAsync(Guid machineId, DeploymentTokens tokens, Guid runId, AgentRunReport report, CancellationToken cancellationToken)
    {
        AgentRunReportResult reported = await server.ReportRunAsync(machineId, tokens.Token, runId, report, cancellationToken).ConfigureAwait(false);

        tokens.Update(reported.Token, reported.ResumeToken);
    }
}
