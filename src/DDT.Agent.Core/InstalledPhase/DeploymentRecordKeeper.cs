// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;

namespace DDT.Agent.InstalledPhase;

// Notes how a run ended in the installed system, and writes the record before the agent removes itself. A run the
// server ended some other way, or a system without a record, gets none.
internal sealed class DeploymentRecordKeeper(IDeploymentRecord? record, string agentVersion, AgentLog log, TimeProvider timeProvider)
{
    private DeploymentRecordEntry? _entry;

    public void Ended(AgentRun run, DeploymentState state)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (record is null || state is not (DeploymentState.Done or DeploymentState.Failed))
        {
            return;
        }

        if (run.SkipDeploymentRecord)
        {
            log.Information("The server's settings leave out the deployment record.");

            return;
        }

        _entry = new DeploymentRecordEntry(
            run.SequenceName,
            run.SequenceId,
            run.SequenceRevision,
            run.Id,
            state.ToString(),
            timeProvider.GetUtcNow(),
            run.ImageName,
            agentVersion,
            run.ServerName);
    }

    // Never the run's end: the run is already over.
    public async Task WriteAsync()
    {
        if (_entry is not { } entry || record is null)
        {
            return;
        }

        _entry = null;

        try
        {
            await record.WriteAsync(entry, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            log.Warning($"The deployment record could not be written ({exception.Message}).");
        }
    }
}
