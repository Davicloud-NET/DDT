// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;

namespace DDT.Agent.InstalledPhase;

// Ends the service: waits for the restart, or removes the agent once the run is over.
internal sealed class InstalledPhaseExit(
    IAgentRemoval removal,
    InstalledPhaseRestart restart,
    InstalledPhaseConsole console,
    InstalledPhaseProfile profile,
    AgentLog log)
{
    // The state already says the step before the restart is done, so nothing may continue before the restart. Returns
    // null when none is due.
    public async Task<int?> RestartIfDueAsync(CancellationToken cancellationToken)
    {
        if (!restart.IsDue)
        {
            return null;
        }

        log.Warning(profile.Texts.RestartWasDue);
        await restart.RestartAsync(cancellationToken).ConfigureAwait(false);

        return await restart.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // Called before the run tells the server, so a service that stops or dies before the restart does it at its next
    // start.
    public void RecordRestart() => restart.Record();

    public Task<int> WaitForRestartAsync(CancellationToken cancellationToken) => restart.WaitAsync(cancellationToken);

    // Called after the server has the run's last report.
    public Task<int> EndAsync(AgentRunReport report, CancellationToken cancellationToken) =>
        report.State == DeploymentState.Done
            ? RemoveAndRestartAsync(cancellationToken)
            : RemoveAsync(AgentExitCodes.Stopped, signOut: false, cancellationToken);

    // After a failure the session waits for someone to sign out, and a stop may end that wait. The next start then
    // finds the run over and ends what's left.
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

    // In Windows the agent and the log it holds open are only deleted when Windows next starts, so it restarts once
    // more. A service that starts once more after that finds no run and only removes itself, so this restart never
    // leads to another.
    public async Task<int> RemoveAndRestartAsync(CancellationToken cancellationToken)
    {
        if (!await console.EndAsync(signOut: true, cancellationToken).ConfigureAwait(false))
        {
            log.Information("The agent was stopped while DDT's session ended. It ends it and removes itself when it next starts.");

            return AgentExitCodes.Deployed;
        }

        await removal.RemoveAsync(CancellationToken.None).ConfigureAwait(false);

        if (!profile.RestartsAfterRemoval)
        {
            return AgentExitCodes.Deployed;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            log.Information(profile.Texts.StoppedAfterRemoval);

            return AgentExitCodes.Deployed;
        }

        await restart.RestartOnceMoreAsync(cancellationToken).ConfigureAwait(false);

        return AgentExitCodes.Deployed;
    }
}
