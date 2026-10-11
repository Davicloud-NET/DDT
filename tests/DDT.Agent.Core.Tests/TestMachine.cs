// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;

namespace DDT.Agent.Tests;

// The machine TestAgents.Loop runs on: the fakes, the log and the clock. Without Identity it is the dry run's machine 1,
// and without Runner it gets the runner of the loop's own helper.
internal sealed record TestMachine(FakeDeploymentTools Tools, AgentLog Log, TimeProvider Time)
{
    public IMachineIdentityReader? Identity { get; init; }

    public SequenceRunner? Runner { get; init; }

    // The boot environment the machine started, and its way into the other one, if it has any.
    public AgentEnvironment Environment { get; init; } = AgentEnvironment.WindowsPE;

    public EnvironmentSwitch? Switch { get; init; }

    // What the loop calls when it registers again where it could have ended.
    public Action? GoesOn { get; init; }

    // What a look at the disks before the loop left it: that someone started over on purpose, and a resume token.
    public bool StartsOver { get; init; }

    public string? ResumeToken { get; init; }
}
