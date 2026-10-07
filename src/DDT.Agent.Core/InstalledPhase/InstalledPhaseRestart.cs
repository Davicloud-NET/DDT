// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;

namespace DDT.Agent.InstalledPhase;

// The restarts a run asks for in the installed system. The marker keeps one due until it happens.
internal sealed class InstalledPhaseRestart(
    IRebooter rebooter,
    IRestartMarker marker,
    IInstalledPhaseTexts texts,
    AgentLog log,
    TimeProvider timeProvider,
    bool dryRun)
{
    public bool IsDue => marker.IsSet;

    public void Record() => marker.Set();

    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await rebooter.RebootAsync(RestartInto.InstalledSystem, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            log.Error(texts.CannotRestart(LogText.OneLine(exception)));
        }
    }

    // The restart stops the service. Until then there's nothing to do but ask again now and then, in case the restart
    // never comes. A dry run has no service to stop.
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
                await Task.Delay(InstalledPhaseLoop.RestartTimeout, timeProvider, cancellationToken).ConfigureAwait(false);
                log.Warning(texts.NotRestarted(InstalledPhaseLoop.RestartTimeout));
                await RestartAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AgentExitCodes.Restarting;
        }
    }

    // Called after the agent removed itself. Windows deletes the files still in use as it starts.
    public async Task RestartOnceMoreAsync(CancellationToken cancellationToken)
    {
        log.Information(texts.RestartsOnceMore);

        try
        {
            await rebooter.RebootAsync(RestartInto.InstalledSystem, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            log.Warning(texts.CannotRestartOnceMore(LogText.OneLine(exception)));
        }
    }
}
