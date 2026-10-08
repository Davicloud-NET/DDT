// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

// Restarts the machine from the Linux boot environment. For a restart back into it the network starts next whatever
// the boot order says. A restart into the installed system follows the boot order, where the run put the disk first.
public sealed class LinuxBootRebooter(IUefiVariables variables, LinuxRestart restart, AgentLog log) : IRebooter
{
    private readonly NextStart _next = new(variables, log, AgentEnvironment.LinuxBoot);

    public Task RebootAsync(RestartInto into, CancellationToken cancellationToken)
    {
        if (into == RestartInto.BootEnvironment)
        {
            _next.FromThisEntry();
        }
        else
        {
            _next.FromTheBootOrder();
        }

        // The loop still sends its last lines after this, so they say what was unmounted.
        restart.LetGo();
        restart.Issue();

        return Task.CompletedTask;
    }

    public void LetGo() => restart.LetGo();

    // The agent goes on after it issued a restart, as when the server refuses its token with the last lines. Nothing
    // set for that restart stays: not the restart, not the next start, not the token in the firmware.
    public void Withdraw(ResumeCarrier carrier)
    {
        ArgumentNullException.ThrowIfNull(carrier);

        if (!restart.Withdraw())
        {
            return;
        }

        log.Warning("The agent goes on, so the restart it had issued does not happen.");
        _next.FromTheBootOrder();
        carrier.Withdraw();
    }
}
