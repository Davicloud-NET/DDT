// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

// Restarts the machine from the Linux boot environment. For a restart back into it the network starts next whatever
// the boot order says. A restart into the installed system is plain: the run put the disk first.
public sealed class LinuxBootRebooter(IUefiVariables variables, LinuxRestart restart, AgentLog log) : IRebooter
{
    private readonly NextStart _next = new(variables, log, AgentEnvironment.LinuxBoot);

    public Task RebootAsync(RestartInto into, CancellationToken cancellationToken)
    {
        if (into == RestartInto.BootEnvironment)
        {
            _next.FromThisEntry();
        }

        restart.Issue();

        return Task.CompletedTask;
    }
}
