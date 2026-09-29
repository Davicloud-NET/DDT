// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;

namespace DDT.Agent.Sequences;

// Checks a fresh run, or finds a resumed run's volumes again. A fresh run is reported to the server as running before
// anything changes on any disk. A resumed run is running already, and its first beat says where it is.
internal sealed class RunStart(
    RunPreflight preflight,
    IDiskPartitioner partitioner,
    RunEnding ending,
    ConsoleStatus? status,
    AgentLog log,
    TimeProvider timeProvider)
{
    // Returns null once the run may continue with its steps. Otherwise returns how it ended before them.
    public async Task<RunResult?> StartAsync(SequenceRun run, CancellationToken cancellationToken)
    {
        // A tree counts the steps on all its branches, as they are before the run takes any of them.
        int count = SequenceTree.Leaves(run.Session.Run.Sequence).Count;
        bool prepared = false;

        try
        {
            await PrepareAsync(run, count, cancellationToken).ConfigureAwait(false);
            prepared = true;
            await ReportRunningAsync(run, cancellationToken).ConfigureAwait(false);

            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(RunOutcome.Stopped);
        }
        catch (AgentTokenRejectedException)
        {
            log.Warning("The server no longer accepts this machine's token. Registering again; nothing was changed on any disk.");

            return new RunResult(RunOutcome.TokenRejected);
        }
        catch (Exception exception) when (!prepared)
        {
            return await CannotStartAsync(run, exception, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            string message = $"The server did not let the run start: {LogText.OneLine(exception)} Nothing was changed on any disk.";
            log.Error(message);
            status?.RunFailed(message);

            return new RunResult(RunOutcome.Failed);
        }
    }

    private async Task PrepareAsync(SequenceRun run, int count, CancellationToken cancellationToken)
    {
        AgentRun agentRun = run.Session.Run;

        if (run.Resumed is not { } resumed)
        {
            log.Information($"The run of {agentRun.SequenceName} begins: {(count == 1 ? "1 step" : $"{count} steps")}.");
            await preflight.CheckAsync(run.Session, run.Request.ConfirmedDisk, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            log.Information($"The run of {agentRun.SequenceName} goes on {WhereItGoesOn(run.State, count)}.");
            await ResumeAsync(run, resumed, cancellationToken).ConfigureAwait(false);
        }
    }

    // A run that waits for answers to its inputs says so from its first report.
    private async Task ReportRunningAsync(SequenceRun run, CancellationToken cancellationToken)
    {
        if (run.Resumed is not null)
        {
            return;
        }

        if (run.WaitsForInputs)
        {
            run.Heartbeat.Activity = RunActivity.WaitingForInput;
        }

        await ServerCallRules.CallAsync(
            call => run.Heartbeat.ReportAsync(run.Heartbeat.Snapshot(DeploymentState.Running), call),
            "the start of the run",
            log,
            timeProvider,
            cancellationToken).ConfigureAwait(false);
    }

    // WinPE picked a letter for the Windows volume and gave the other partitions none, so they're found by their ids.
    // Before Partition finished there are no ids, and the engine fails the interrupted Partition. The installed Windows
    // runs from the run's Windows volume, and its steps need no other volume.
    private async Task ResumeAsync(SequenceRun run, LocalRun resumed, CancellationToken cancellationToken)
    {
        if (run.InWindows)
        {
            run.Session.RunningWindows = resumed.WindowsRoot;
        }
        else if (RunVariables.DiskIds(resumed.State.Variables) is { } ids)
        {
            run.Session.Volumes = await partitioner.FindAsync(ids, resumed.WindowsRoot, cancellationToken).ConfigureAwait(false);
        }

        await run.Store.AttachAsync(resumed.Files, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<RunResult> CannotStartAsync(SequenceRun run, Exception exception, CancellationToken cancellationToken)
    {
        string message = LogText.OneLine(exception);
        log.Error(run.Resumed is null ? $"The run cannot start: {message}" : $"The run cannot go on: {message}");
        status?.RunFailed(message);
        AgentRunReport report = run.Heartbeat.Snapshot(DeploymentState.Failed, message);

        if (run.Resumed is { } resumed)
        {
            await ending.EndRunAsync(run, resumed.Files, report).ConfigureAwait(false);
            LocalRun.DeleteAnswerFile(resumed.State, resumed.WindowsRoot, log);
        }

        return await ending.ReportFailedAsync(run, run.Resumed?.Files, report, cancellationToken).ConfigureAwait(false);
    }

    // At the step the state names, by number in a list and by name in a tree.
    private static string WhereItGoesOn(SequenceState state, int count)
    {
        if (state.Format < SequenceState.TreeFormat)
        {
            return $"at step {Math.Min(state.NextIndex + 1, count)} of {count}";
        }

        return state.Cursor is { } cursor && SequenceTree.Index(state.Definition).TryGetValue(cursor.NodeId, out NodePosition? position)
            ? cursor.Leaving ? $"after the steps of {position.Step.Name}" : $"at step {position.Step.Name}"
            : "at its end";
    }
}
