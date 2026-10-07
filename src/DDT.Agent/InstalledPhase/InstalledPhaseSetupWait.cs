// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;

namespace DDT.Agent.InstalledPhase;

// The system's own setup comes before the run's next step, however long someone takes at Windows' out-of-box
// experience. A step that ran during setup would be cut off by its restarts. The heartbeat keeps the run's page up to
// date meanwhile.
internal sealed class InstalledPhaseSetupWait(
    InstalledPhaseProfile profile,
    RunHeartbeatFactory heartbeats,
    InstalledPhaseConsole console,
    AgentLog log,
    TimeProvider timeProvider)
{
    private readonly ISetupProbe _setup = profile.Setup;
    private readonly IInstalledPhaseTexts _texts = profile.Texts;

    // Called at every start of the service, before anything that can take long, because setup may sign in to DDT's
    // session any moment.
    public Task PrepareSessionAsync(CancellationToken cancellationToken) => console.PrepareAsync(cancellationToken);

    // Once setup has finished, the session takes over the sign-in settings and the answer file is deleted. Returns
    // false when the server didn't take the heartbeat's report meanwhile. The next registration sorts that out.
    public async Task<bool> WaitAsync(InstalledPhaseContact contact, AgentRun run, LocalRun local, CancellationToken cancellationToken)
    {
        if (_setup.Pending() is { } pending && !await WaitUntilFinishedAsync(pending, contact, run, local, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        console.SetupFinished();
        DeleteAnswerFile(local);

        return true;
    }

    private async Task<bool> WaitUntilFinishedAsync(
        string pending,
        InstalledPhaseContact contact,
        AgentRun run,
        LocalRun local,
        CancellationToken cancellationToken)
    {
        log.Information(_texts.WaitingForSetup(pending));
        console.Status?.WaitingForSetup(run, local.State, profile.WaitActivity);

        (FileRunStateStore store, RunHeartbeat heartbeat) = heartbeats.Create(new RunSession(contact.MachineId, run, contact.Tokens));
        await store.AttachAsync(local.Files, cancellationToken).ConfigureAwait(false);
        heartbeat.Update(local.State);
        heartbeat.Activity = profile.WaitActivity;

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
            log.Warning(_texts.ReportRefusedDuringSetup(LogText.OneLine(failure)));

            return false;
        }

        log.Information(_texts.SetupFinished);

        return true;
    }

    // Warns now and then, because setup may wait for someone at the machine.
    private async Task PollAsync(string? pending, CancellationToken cancellationToken)
    {
        long started = timeProvider.GetTimestamp();
        TimeSpan warnAfter = InstalledPhaseLoop.SetupWarningInterval;

        while (pending is not null)
        {
            await Task.Delay(InstalledPhaseLoop.SetupPollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
            pending = _setup.Pending();
            TimeSpan waited = timeProvider.GetElapsedTime(started);

            if (pending is not null && waited >= warnAfter)
            {
                log.Warning(_texts.SetupTakesLong(LogText.Duration(waited), pending));
                warnAfter += InstalledPhaseLoop.SetupWarningInterval;
            }
        }
    }

    // Windows setup has read the answer file, and its SetupComplete.cmd line may never have run, because an OEM key
    // skips it. A system without an answer file has none to delete.
    private void DeleteAnswerFile(LocalRun local)
    {
        string path = UnattendFile.PathIn(local.Root);
        bool there = File.Exists(path);
        LocalRun.DeleteAnswerFile(local.State, local.Root, log);

        if (there && !File.Exists(path))
        {
            log.Information($"Deleted the answer file {path}, which holds passwords.");
        }
    }
}
