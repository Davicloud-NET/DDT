// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;

namespace DDT.Agent;

// This agent and the machine it runs on, as the loop sees them. A dry run and the tests fake the machine.
public sealed record AgentMachine(
    IMachineIdentityReader Identity,
    IDisks Disks,
    LocalRunLocator Runs,
    string AgentVersion,
    AgentEnvironment Environment)
{
    // How the machine gets into the other boot environment. Null where nothing restarts it for that.
    public EnvironmentSwitch? Switch { get; init; }

    // The resume token of a registration this start made before its loop, as for a run it found handed over.
    public string? ResumeToken { get; init; }
}
