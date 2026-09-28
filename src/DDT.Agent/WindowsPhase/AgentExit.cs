// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;

namespace DDT.Agent.WindowsPhase;

// How the service ends: waiting for a restart of Windows, or with the agent's removal once the run is over. DDT's
// session ends first, and nothing may stop the removal half way.
internal sealed class AgentExit(IAgentRemoval removal, WindowsRestart restart, WindowsPhaseConsole console, AgentLog log)
{
    // The state already says the step before the restart is done, so nothing may go on before the restart. Null when
    // none is due.
    public async Task<int?> RestartIfDueAsync(CancellationToken cancellationToken)
    {
        if (!restart.IsDue)
        {
            return null;
        }

        log.Warning("Windows was to restart for the run but has not restarted since. Restarting it now.");
        await restart.RestartAsync(cancellationToken).ConfigureAwait(false);

        return await restart.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // Before the run tells the server, so a service that stops or dies before the restart makes it at its next start.
    public void RecordRestart() => restart.Record();

    public Task<int> WaitForRestartAsync(CancellationToken cancellationToken) => restart.WaitAsync(cancellationToken);

    // After the server has the run's last report: Windows restarts once more after a finished run.
    public Task<int> EndAsync(AgentRunReport report, CancellationToken cancellationToken) =>
        report.State == DeploymentState.Done
            ? RemoveAndRestartAsync(cancellationToken)
            : RemoveAsync(AgentExitCodes.Stopped, signOut: false, cancellationToken);

    // After a failure the session waits for someone to sign out, and a stop may end that wait: the next start finds the
    // run over and ends what is left.
    public async Task<int> RemoveAsync(int exitCode, bool signOut, CancellationToken cancellationToken)
    {
        if (!await console.EndAsync(signOut, cancellationToken).ConfigureAwait(false))
        {
            log.Information("The agent was stopped while DDT's session ended. It ends it and removes itself when it next starts.");

            return AgentExitCodes.Stopped;
        }

        await removal.RemoveAsync(CancellationToken.None).ConfigureAwait(false);

        return exitCode;
    }

    // The agent and the log it holds open go only when Windows next starts. A service that starts once more after it
    // finds no run and only removes itself, so this restart never leads to another.
    public async Task<int> RemoveAndRestartAsync(CancellationToken cancellationToken)
    {
        if (!await console.EndAsync(signOut: true, cancellationToken).ConfigureAwait(false))
        {
            log.Information("The agent was stopped while DDT's session ended. It ends it and removes itself when it next starts.");

            return AgentExitCodes.Deployed;
        }

        await removal.RemoveAsync(CancellationToken.None).ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            log.Information("The agent was stopped. What is left of it goes when Windows next starts.");

            return AgentExitCodes.Deployed;
        }

        await restart.RestartOnceMoreAsync(cancellationToken).ConfigureAwait(false);

        return AgentExitCodes.Deployed;
    }
}
