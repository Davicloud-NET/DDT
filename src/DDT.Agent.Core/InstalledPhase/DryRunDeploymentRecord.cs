// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.InstalledPhase;

// Writes nothing, because the registry is this computer's.
public sealed class DryRunDeploymentRecord(AgentLog log) : IDeploymentRecord
{
    public Task WriteAsync(DeploymentRecordEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        log.Information($"Dry run: in Windows, the deployment record would say {entry.Result} for run {entry.RunId:D} of {entry.SequenceName}.");

        return Task.CompletedTask;
    }
}
