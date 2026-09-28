// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;

namespace DDT.Agent.Sequences;

// How a run's phase ends: done, restarting, refused or failed. The order of the last log lines, the reports, the boot
// order and the restart marker keeps a machine from starting what the run did not finish, so it must not change.
internal sealed class RunEnding(
    RunBootOrder bootOrder,
    IRebooter rebooter,
    WindowsPERestartMarker restartMarker,
    AgentLog log,
    TimeProvider timeProvider,
    ConsoleStatus? status)
{
    private const int MaxFinalFlushes = 20;
    private const int MaxFinalFlushFailures = 3;

    // Recorded as soon as the engine asks for a restart, whose step the state already has as done.
    public void RecordWindowsPERestart() => restartMarker.Set(RestartInto.WindowsPE);

    // A restart Windows PE recorded that never happened, as the agent was stopped or wpeutil failed, comes before
    // anything else. Null when none is due; a start that is stopped already leaves it due.
    public async Task<RunOutcome?> RestartIfDueAsync(CancellationToken cancellationToken)
    {
        if (restartMarker.Due is not { } into)
        {
            return null;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return RunOutcome.Stopped;
        }

        log.Warning(into == RestartInto.WindowsPE
            ? "The machine was to restart into Windows PE for the run but has not restarted since. Restarting it now."
            : "The machine was to start the installed Windows but has not restarted since. Restarting it now.");

        RunResult result = await RebootAsync(into, RestartReason.WasDue, RunOutcome.Restarting, "Restart it by hand.", cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome;
    }

    // The log goes while the machine may still send it, and the Done report is the last call the server gets. In
    // Windows there is no restart here: the agent removes itself first, and the Done report waits on the disk with the
    // token until the server has it.
    public async Task<RunResult> FinishAsync(SequenceRun run, CancellationToken cancellationToken)
    {
        log.Information(run.InWindows ? "The run is done. Sending the last log lines." : "The run is done. Sending the last log lines, then restarting.");
        status?.RunFinished();

        AgentRunReport done = run.Heartbeat.Snapshot(DeploymentState.Done);
        await EndRunAsync(run, run.Store.Files, done).ConfigureAwait(false);

        if (!run.InWindows && run.Session.RunDirectory is { } directory)
        {
            Leftovers.Delete(directory, log);
        }

        if (await SendDoneAsync(run, done, cancellationToken).ConfigureAwait(false) is { } instead)
        {
            return instead;
        }

        Reported(run, run.Store.Files);

        if (run.InWindows)
        {
            return new RunResult(RunOutcome.Finished);
        }

        return await RebootAsync(
            RestartInto.Windows,
            RestartReason.RunDone,
            RunOutcome.Finished,
            "Restart it by hand; the run is done.",
            cancellationToken).ConfigureAwait(false);
    }

    // The state and token are on the disk for the next start. After the hand-over the installed Windows would go on
    // with the run and use its answer file, so there a refused token keeps it from starting.
    public async Task<RunResult> RestartAsync(SequenceRun run, RestartInto into, CancellationToken cancellationToken)
    {
        bool handingOver = !run.InWindows && into == RestartInto.Windows;
        run.Heartbeat.Activity = RunActivity.Restarting;
        log.Information(run.InWindows
            ? "Windows restarts, and the run goes on after the restart."
            : handingOver
                ? "The machine restarts into the installed Windows, where the agent goes on with the run."
                : "The machine restarts into Windows PE, and the run goes on after the restart.");

        try
        {
            await ServerCallRules.CallAsync(
                call => run.Heartbeat.ReportAsync(run.Heartbeat.Snapshot(DeploymentState.Running), call),
                "the restart",
                log,
                timeProvider,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(RunOutcome.Stopped);
        }
        catch (AgentTokenRejectedException) when (handingOver)
        {
            return await TokenRejectedAsync(run).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            log.Warning($"The server could not be told of the restart ({LogText.OneLine(exception)}). The machine restarts anyway.");
        }

        if (!await FlushAllAsync(run.Heartbeat, cancellationToken).ConfigureAwait(false) && handingOver)
        {
            return await TokenRejectedAsync(run).ConfigureAwait(false);
        }

        return await RebootAsync(
            into,
            handingOver ? RestartReason.HandOver : RestartReason.StepAsked,
            RunOutcome.Restarting,
            "Restart it by hand; the run goes on after the restart.",
            cancellationToken).ConfigureAwait(false);
    }

    // The state and answer file stay for the registration with the run token to decide on; until then the machine
    // starts from the network.
    public async Task<RunResult> TokenRejectedAsync(SequenceRun run)
    {
        await bootOrder.RestoreAsync(run).ConfigureAwait(false);
        log.Warning("The server no longer accepts this machine's token during the run. Registering again.");

        return new RunResult(RunOutcome.TokenRejected, run.Heartbeat.Snapshot(DeploymentState.Failed, SequenceRunner.LostContactMessage));
    }

    public async Task<RunResult> RebootAsync(
        RestartInto into,
        RestartReason reason,
        RunOutcome outcome,
        string byHand,
        CancellationToken cancellationToken)
    {
        status?.Restarting(reason, into);

        try
        {
            await rebooter.RebootAsync(into, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(RunOutcome.Stopped);
        }
        catch (Exception exception)
        {
            string message = $"The machine could not restart itself ({LogText.OneLine(exception)}). {byHand}";
            log.Error(message);
            status?.RestartFailed(message);
        }

        return new RunResult(outcome);
    }

    // The run is over: nothing of it may start Windows or resume it.
    public async Task<RunResult> FailAsync(SequenceRun run, string error, CancellationToken cancellationToken)
    {
        log.Error($"The run failed: {error}");
        status?.RunFailed(error);
        await UndoAsync(run).ConfigureAwait(false);
        AgentRunReport report = run.Heartbeat.Snapshot(DeploymentState.Failed, error);
        await EndRunAsync(run, run.Store.Files, report).ConfigureAwait(false);
        await FlushAllAsync(run.Heartbeat, cancellationToken).ConfigureAwait(false);

        return await ReportFailedAsync(run, run.Store.Files, report, cancellationToken).ConfigureAwait(false);
    }

    // A failure the server was not told of goes back to the loop, which reports it once it can.
    public async Task<RunResult> ReportFailedAsync(SequenceRun run, RunFiles? files, AgentRunReport report, CancellationToken cancellationToken)
    {
        try
        {
            await ServerCallRules.CallAsync(
                call => run.Heartbeat.ReportAsync(report, call),
                "the failure report",
                log,
                timeProvider,
                cancellationToken).ConfigureAwait(false);

            Reported(run, files);

            return new RunResult(RunOutcome.Failed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(RunOutcome.Stopped);
        }
        catch (AgentTokenRejectedException)
        {
            log.Warning("The server no longer accepts this machine's token, so the failure could not be reported. Registering again.");

            return new RunResult(RunOutcome.TokenRejected, report);
        }
        catch (Exception exception)
        {
            log.Warning($"The failure could not be reported ({LogText.OneLine(exception)}).");

            return new RunResult(RunOutcome.Failed, report);
        }
    }

    // In Windows PE the run's files go at once, token first. In Windows the token stays with the report until the server
    // has it, as only the next start could tell the server after a stop; if the report cannot be kept, the files go too.
    public async Task EndRunAsync(SequenceRun run, RunFiles? files, AgentRunReport report)
    {
        if (files is null)
        {
            return;
        }

        if (run.InWindows)
        {
            try
            {
                await files.SaveFinalReportAsync(report, CancellationToken.None).ConfigureAwait(false);

                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                log.Warning($"The run's last report could not be kept on the disk ({exception.Message}), so it is lost should the agent stop before the server has it.");
            }
        }

        files.Discard();
    }

    // Null once the server has the Done report or cannot be told; otherwise how the run ends instead.
    private async Task<RunResult?> SendDoneAsync(SequenceRun run, AgentRunReport done, CancellationToken cancellationToken)
    {
        try
        {
            // A refused token before the Done report means the run was stopped meanwhile, so the machine must not start
            // into a Windows with the answer file.
            if (!await FlushAllAsync(run.Heartbeat, cancellationToken).ConfigureAwait(false))
            {
                await UndoAsync(run).ConfigureAwait(false);
                log.Warning("The server no longer accepts this machine's token, so the run was stopped before it ended. Registering again.");

                return new RunResult(RunOutcome.TokenRejected);
            }

            await ServerCallRules.CallAsync(
                call => run.Heartbeat.ReportAsync(done, call),
                "the end of the run",
                log,
                timeProvider,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(RunOutcome.Stopped);
        }
        catch (AgentTokenRejectedException)
        {
            // The server stores Done before it answers, so a lost answer makes the retry look like this.
            log.Warning("The server no longer accepts this machine's token. It most likely recorded the run as done already.");
        }
        catch (DeploymentStepException exception) when (run.InWindows && exception.InnerException is { } cause && !ServerCallRules.IsRefusal(cause))
        {
            log.Warning($"The server could not be told that the run is done ({LogText.OneLine(exception)}). It is told once it can be reached.");

            return new RunResult(RunOutcome.Finished, done);
        }
        catch (Exception exception)
        {
            log.Warning($"The server could not be told that the run is done ({LogText.OneLine(exception)}). " +
                (run.InWindows ? "The agent removes itself anyway." : "The machine restarts anyway."));
        }

        return null;
    }

    // The server has the run's last report, or will never take it.
    private static void Reported(SequenceRun run, RunFiles? files)
    {
        if (run.InWindows)
        {
            files?.Discard();
        }
    }

    // The answer file holds passwords, and a machine whose run did not finish must start from the network until it runs
    // again, not into a Windows without its answer file. A restart into Windows PE the run asked for is not due either.
    private async Task UndoAsync(SequenceRun run)
    {
        if (run.Session.Volumes is { } volumes)
        {
            LocalRun.DeleteAnswerFile(run.State, volumes.Windows, log);
        }

        if (!run.InWindows)
        {
            restartMarker.Clear();
        }

        await bootOrder.RestoreAsync(run).ConfigureAwait(false);
    }

    // Until the queue is empty, but never for long: the lines are worth less than the restart that follows. False when
    // the server refused the token.
    private async Task<bool> FlushAllAsync(RunHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        int failures = 0;

        for (int attempt = 0; attempt < MaxFinalFlushes && log.QueuedLines > 0; attempt++)
        {
            try
            {
                await heartbeat.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (AgentTokenRejectedException)
            {
                return false;
            }
            catch (Exception exception) when (ServerCallRules.IsRefusal(exception))
            {
                return true;
            }
            catch (Exception exception) when (ServerCallRules.IsTransient(exception, cancellationToken))
            {
                if (++failures > MaxFinalFlushFailures)
                {
                    return true;
                }

                await Task.Delay(AgentLimits.RetryDelay(failures), timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return true;
            }
        }

        return true;
    }
}
