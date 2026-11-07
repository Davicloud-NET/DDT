// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;

namespace DDT.Agent;

// One registration's polling state: its tokens, what the server last reported, and when to poll next.
internal sealed class PollState(AgentRegistrationResult registration, DeploymentTokens tokens)
{
    public Guid MachineId { get; } = registration.MachineId;

    public DeploymentTokens Tokens { get; } = tokens;

    public MachineState State { get; set; } = registration.State;

    public string? SignedInBy { get; set; } = registration.SignedInBy;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(registration.PollAfterSeconds);

    public int Failures { get; set; }

    // Why the machine's assigned run does not start here, as the server last said. Null while it said nothing.
    public string? WaitReason { get; private set; }

    // Every poll repeats the reason, so it goes into the log only when it is new.
    public void Waits(string? reason, AgentLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        if (reason == WaitReason)
        {
            return;
        }

        WaitReason = reason;

        if (reason is not null)
        {
            log.Warning(reason);
        }
    }
}
