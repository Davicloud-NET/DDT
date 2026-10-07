// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

// Says what the boot environment the dry run stands in for would do to restart.
public sealed class DryRunRebooter(AgentLog log, AgentEnvironment environment = AgentEnvironment.WindowsPE) : IRebooter
{
    public Task RebootAsync(RestartInto into, CancellationToken cancellationToken)
    {
        (string where, string how) = environment == AgentEnvironment.LinuxBoot
            ? ("the Linux boot environment", "the machine would restart now")
            : ("Windows PE", "wpeutil reboot would run now");

        log.Information(into == RestartInto.BootEnvironment
            ? $"Dry run: this computer is not restarted. In {where}, BootNext would be set to BootCurrent, so the machine starts " +
                $"from the network again, and {how}."
            : $"Dry run: this computer is not restarted. In {where}, {how}.");

        return Task.CompletedTask;
    }
}
