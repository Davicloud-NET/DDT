// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

public sealed class DryRunRebooter(AgentLog log) : IRebooter
{
    public Task RebootAsync(CancellationToken cancellationToken)
    {
        log.Information("Dry run: this computer is not restarted. In Windows PE, wpeutil reboot would run now.");

        return Task.CompletedTask;
    }
}
