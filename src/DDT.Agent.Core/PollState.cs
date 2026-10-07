// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;

namespace DDT.Agent;

// The polling state of one registration. It holds the tokens, the state and sign-in as the server last reported them,
// and when to poll next.
internal sealed class PollState(AgentRegistrationResult registration, DeploymentTokens tokens)
{
    public Guid MachineId { get; } = registration.MachineId;

    public DeploymentTokens Tokens { get; } = tokens;

    public MachineState State { get; set; } = registration.State;

    public string? SignedInBy { get; set; } = registration.SignedInBy;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(registration.PollAfterSeconds);

    public int Failures { get; set; }
}
