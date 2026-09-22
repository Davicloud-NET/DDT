// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;

namespace DDT.Agent.WindowsPhase;

// Takes the agent off the Windows at windowsRoot once its run is over: the run's token first, so nothing left can act
// as the machine, then its state, so nothing can go on with the run.
public sealed class AgentRemoval(string windowsRoot, AgentLog log) : IAgentRemoval
{
    public Task RemoveAsync(CancellationToken cancellationToken)
    {
        log.Information("The run is over here, so the agent removes itself.");
        RunFiles.In(windowsRoot, log).Discard();

        return Task.CompletedTask;
    }
}
