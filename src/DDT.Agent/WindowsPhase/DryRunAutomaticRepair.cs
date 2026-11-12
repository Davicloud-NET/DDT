// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.InstalledPhase;

namespace DDT.Agent.WindowsPhase;

// Changes nothing, because bcdedit would change how this computer starts.
public sealed class DryRunAutomaticRepair(AgentLog log) : IAutomaticRepair
{
    public Task TurnOffAsync(CancellationToken cancellationToken)
    {
        log.Information("Dry run: in Windows, Automatic Repair would be turned off while the run goes on.");

        return Task.CompletedTask;
    }

    public Task PutBackAsync(CancellationToken cancellationToken)
    {
        log.Information("Dry run: in Windows, Automatic Repair would be put back as it was.");

        return Task.CompletedTask;
    }
}
