// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;

namespace DDT.Agent.WindowsPhase;

// The restarts of Windows a run asks for. The marker keeps one due from the moment the run knows of it until it
// happens, so a service that starts again without it restarts Windows instead of going on.
internal sealed class WindowsRestart(IRebooter rebooter, IRestartMarker marker, AgentLog log, TimeProvider timeProvider, bool dryRun)
{
    public bool IsDue => marker.IsSet;

    public void Record() => marker.Set();

    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await rebooter.RebootAsync(RestartInto.Windows, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            log.Error($"Windows could not restart itself ({LogText.OneLine(exception)}). Restart it by hand; the run goes on after the restart.");
        }
    }

    // The restart stops the service, and until then there is nothing to do but ask again now and then, in case the
    // restart never came. A dry run has no service to stop.
    public async Task<int> WaitAsync(CancellationToken cancellationToken)
    {
        if (dryRun)
        {
            return AgentExitCodes.Restarting;
        }

        try
        {
            while (true)
            {
                await Task.Delay(WindowsPhaseLoop.RestartTimeout, timeProvider, cancellationToken).ConfigureAwait(false);
                log.Warning($"Windows has not restarted {WindowsPhaseLoop.RestartTimeout.TotalMinutes:0} minutes after the run asked it to. Asking again.");
                await RestartAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AgentExitCodes.Restarting;
        }
    }

    // After the agent removed itself: Windows deletes the files still in use as it starts.
    public async Task RestartOnceMoreAsync(CancellationToken cancellationToken)
    {
        log.Information("Windows restarts once more, which deletes what is left of the agent.");

        try
        {
            await rebooter.RebootAsync(RestartInto.Windows, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            log.Warning($"Windows could not restart itself ({LogText.OneLine(exception)}). What is left of the agent goes when Windows next starts.");
        }
    }
}
