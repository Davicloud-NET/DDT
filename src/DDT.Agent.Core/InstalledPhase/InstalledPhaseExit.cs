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
    DeploymentRecordKeeper record,
    AgentLog log)
{
    // True once the agent has removed itself, after which the run's token is gone and nothing more is sent.
    public bool Removed { get; private set; }

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

    // How the run ended here, for the deployment record the removal writes first.
    public void RunEnded(AgentRun run, DeploymentState state) => record.Ended(run, state);

    // Called after the server has the run's last report.
    public Task<int> EndAsync(AgentRunReport report, CancellationToken cancellationToken) =>
        report.State == DeploymentState.Done
            ? RemoveAndRestartAsync(cancellationToken)
            : RemoveAsync(AgentExitCodes.Stopped, signOut: false, cancellationToken);

    // After a failure the session waits for someone to sign out, and a stop may end that wait. The next start then
    // finds the run over and ends what's left.
    public async Task<int> RemoveAsync(int exitCode, bool signOut, CancellationToken cancellationToken)
    {
        await EndRunAsync().ConfigureAwait(false);

        if (!await console.EndAsync(signOut, cancellationToken).ConfigureAwait(false))
        {
            log.Information("The agent was stopped while DDT's session ended. It ends it and removes itself when it next starts.");

            return AgentExitCodes.Stopped;
        }

        Removed = true;
        await removal.RemoveAsync(CancellationToken.None).ConfigureAwait(false);

        return exitCode;
    }

    // In Windows the agent and the log it holds open are only deleted when Windows next starts, so it restarts once
    // more. A service that starts once more after that finds no run and only removes itself, so this restart never
    // leads to another.
    public async Task<int> RemoveAndRestartAsync(CancellationToken cancellationToken)
    {
        await EndRunAsync().ConfigureAwait(false);

        if (!await console.EndAsync(signOut: true, cancellationToken).ConfigureAwait(false))
        {
            log.Information("The agent was stopped while DDT's session ended. It ends it and removes itself when it next starts.");

            return AgentExitCodes.Deployed;
        }

        Removed = true;
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

    // Not stopped by the stop token: the run is over, and Windows must not keep Automatic Repair off.
    private async Task EndRunAsync()
    {
        if (profile.AutomaticRepair is { } repair)
        {
            await repair.PutBackAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await record.WriteAsync().ConfigureAwait(false);
    }
}
