// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;

namespace DDT.Agent.WindowsPhase;

// Windows setup, however long someone takes at the out-of-box experience, comes before the run's next step: a step that
// ran during it would be cut off by its restarts. The heartbeat keeps the run's page up to date meanwhile.
internal sealed class WindowsSetupWait(
    IWindowsSetupProbe setup,
    RunHeartbeatFactory heartbeats,
    WindowsPhaseConsole console,
    AgentLog log,
    TimeProvider timeProvider)
{
    // At every start of the service, before anything that can take long: setup may sign in to DDT's session any moment.
    public Task PrepareSessionAsync(CancellationToken cancellationToken) => console.PrepareAsync(cancellationToken);

    // Once setup has finished, the session takes over the sign-in settings and the answer file goes. False when the
    // server did not take the heartbeat's report meanwhile, which the next registration sorts out.
    public async Task<bool> WaitAsync(WindowsRunContact contact, AgentRun run, LocalRun local, CancellationToken cancellationToken)
    {
        if (setup.Pending() is { } pending && !await WaitUntilFinishedAsync(pending, contact, run, local, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        console.SetupFinished();
        DeleteAnswerFile(local);

        return true;
    }

    private async Task<bool> WaitUntilFinishedAsync(
        string pending,
        WindowsRunContact contact,
        AgentRun run,
        LocalRun local,
        CancellationToken cancellationToken)
    {
        log.Information($"Waiting for Windows setup to finish: {pending}. The run goes on once it has, however long that takes.");
        console.Status?.WaitingForSetup(run, local.State);

        (FileRunStateStore store, RunHeartbeat heartbeat) = heartbeats.Create(new RunSession(contact.MachineId, run, contact.Tokens));
        await store.AttachAsync(local.Files, cancellationToken).ConfigureAwait(false);
        heartbeat.Update(local.State);
        heartbeat.Activity = RunActivity.WaitingForWindowsSetup;

        using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        heartbeat.Start(waiting, cancellationToken);

        try
        {
            await PollAsync(pending, waiting.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (waiting.IsCancellationRequested)
        {
        }
        finally
        {
            await heartbeat.StopAsync().ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (heartbeat.Failure is { } failure)
        {
            log.Warning($"The server did not take the report while Windows setup runs ({LogText.OneLine(failure)}). Registering again.");

            return false;
        }

        log.Information("Windows setup has finished.");

        return true;
    }

    // Warns now and then, as setup may wait for someone at the machine.
    private async Task PollAsync(string? pending, CancellationToken cancellationToken)
    {
        long started = timeProvider.GetTimestamp();
        TimeSpan warnAfter = WindowsPhaseLoop.SetupWarningInterval;

        while (pending is not null)
        {
            await Task.Delay(WindowsPhaseLoop.SetupPollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
            pending = setup.Pending();
            TimeSpan waited = timeProvider.GetElapsedTime(started);

            if (pending is not null && waited >= warnAfter)
            {
                log.Warning($"Windows setup has not finished after {LogText.Duration(waited)}: {pending}. If it waits for someone at the machine, finish it there.");
                warnAfter += WindowsPhaseLoop.SetupWarningInterval;
            }
        }
    }

    // Setup has read the answer file, and its SetupComplete.cmd line may never have run: an OEM key skips it.
    private void DeleteAnswerFile(LocalRun local)
    {
        string path = UnattendFile.PathIn(local.WindowsRoot);
        bool there = File.Exists(path);
        LocalRun.DeleteAnswerFile(local.State, local.WindowsRoot, log);

        if (there && !File.Exists(path))
        {
            log.Information($"Deleted the answer file {path}, which holds passwords.");
        }
    }
}
