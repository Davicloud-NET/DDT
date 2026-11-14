// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.WindowsFeatures;

// Enables nothing, and takes every feature as needing a restart, so a dry run goes through the step's restart.
public sealed class DryRunWindowsFeatures(AgentLog log) : IWindowsFeatures
{
    public Task<FeatureOutcome> EnableAsync(string name, CancellationToken cancellationToken)
    {
        log.Information($"Dry run: {name} is not enabled, and taken as enabled once the machine restarts.");

        return Task.FromResult(FeatureOutcome.RestartNeeded);
    }
}
