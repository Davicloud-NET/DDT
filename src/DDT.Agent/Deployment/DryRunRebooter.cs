// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

public sealed class DryRunRebooter(AgentLog log) : IRebooter
{
    public Task RebootAsync(RestartInto into, CancellationToken cancellationToken)
    {
        log.Information(into == RestartInto.WindowsPE
            ? "Dry run: this computer is not restarted. In Windows PE, BootNext would be set to BootCurrent, so the machine starts " +
                "from the network again, and wpeutil reboot would run now."
            : "Dry run: this computer is not restarted. In Windows PE, wpeutil reboot would run now.");

        return Task.CompletedTask;
    }
}
