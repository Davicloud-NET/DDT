// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.LinuxPhase;

// Restarts the installed Linux for the run through systemd, which stops the agent's unit on its way and starts it
// again afterwards. The boot order is left alone: the run put the disk first before the hand-over.
public sealed class LinuxPhaseRebooter(IToolRunner tools, AgentLog log) : IRebooter
{
    public const string Systemctl = "systemctl";

    // What a system offers when systemd's own tool does not answer.
    public const string Fallback = "reboot";

    public async Task RebootAsync(RestartInto into, CancellationToken cancellationToken)
    {
        try
        {
            await tools.RunAsync(Systemctl, ["reboot"], cancellationToken).ConfigureAwait(false);
        }
        catch (DeploymentStepException exception)
        {
            log.Warning($"{Systemctl} reboot did not restart Linux ({exception.Message}), so {Fallback} is asked.");
            await tools.RunAsync(Fallback, [], cancellationToken).ConfigureAwait(false);
        }
    }
}
