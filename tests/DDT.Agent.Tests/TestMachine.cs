// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;

namespace DDT.Agent.Tests;

// The machine TestAgents.Loop runs on: the fakes, the log and the clock. Without Identity it is the dry run's machine 1,
// and without Runner it gets a dry run's runner.
internal sealed record TestMachine(FakeDeploymentTools Tools, AgentLog Log, TimeProvider Time)
{
    public IMachineIdentityReader? Identity { get; init; }

    public SequenceRunner? Runner { get; init; }
}
