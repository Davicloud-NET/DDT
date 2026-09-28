// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.WindowsPhase;

namespace DDT.Agent.Tests;

// What a test changes of the service WindowsPhaseLoopTestBase runs; null keeps the fake's.
internal sealed record ServiceRunOptions
{
    public TimeProvider? Time { get; init; }

    public AgentLog? Log { get; init; }

    public bool DryRun { get; init; }

    public IAgentRemoval? Removal { get; init; }

    public IDeploySession? Session { get; init; }

    public ConsoleStatus? Status { get; init; }
}
