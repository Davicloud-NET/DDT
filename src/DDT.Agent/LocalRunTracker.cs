// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;

namespace DDT.Agent;

// The run an earlier start of the agent left on the disk, and the newest run token: from the disk, a registration or
// the run itself.
internal sealed class LocalRunTracker(LocalRunLocator locator, AgentLog log)
{
    private LocalRun? _localRun;

    public string? RunToken { get; set; }

    // Looked for before every registration, so a run that a refused token interrupted is found again too.
    public async Task FindAsync(CancellationToken cancellationToken)
    {
        Guid? known = _localRun?.State.RunId;
        _localRun = locator.Find() is { } root ? await LocalRun.LoadAsync(root, log, cancellationToken).ConfigureAwait(false) : null;

        if (_localRun is { } local)
        {
            RunToken = local.RunToken ?? RunToken;

            if (local.State.RunId != known)
            {
                log.Information($"Found run {local.State.RunId} on {local.WindowsRoot}. It goes on if the server still runs it.");
            }
        }
    }

    // The server resumes a run only for the run token of its active run, and says so with the run's id.
    public void KeepOrDiscard(AgentRegistrationResult registration)
    {
        if (_localRun is { } local && registration.RunId != local.State.RunId)
        {
            local.Discard(log);
            log.Information($"The server ended run {local.State.RunId}; its state on disk was removed.");
            _localRun = null;
        }

        RunToken = registration.RunId is null ? null : registration.RunToken ?? RunToken;
    }

    // The run on the disk, when the server still runs it.
    public LocalRun? ResumableFor(AgentRun? run) =>
        run is { State: DeploymentState.Running } && _localRun is { } local && local.State.RunId == run.Id ? local : null;

    // A run starts or goes on, and keeps its own state from now on.
    public void Forget() => _localRun = null;
}
