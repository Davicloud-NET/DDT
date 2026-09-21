// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

internal static class TestAgents
{
    public const string Version = "1.0.0-test";

    // Without a heartbeat interval, beats happen only when the step changes, so a run stays sequential on
    // ImmediateTimeProvider.
    public static DeploymentRunner Runner(
        IAgentServer server,
        FakeDeploymentTools tools,
        AgentLog log,
        TimeProvider timeProvider,
        TimeSpan? heartbeatInterval = null,
        string? scratchDirectory = null,
        IBcdWriter? bcdWriter = null) =>
        new(server, tools, tools, bcdWriter ?? tools, tools, log, timeProvider, heartbeatInterval ?? Timeout.InfiniteTimeSpan, scratchDirectory);

    public static AgentLoop Loop(
        IAgentServer server,
        ISignInPrompt prompt,
        FakeDeploymentTools tools,
        AgentLog log,
        TimeProvider timeProvider,
        IMachineIdentityReader? identity = null) =>
        new(
            server,
            identity ?? new DryRunMachineIdentityReader(1),
            prompt,
            tools,
            Runner(server, tools, log, timeProvider),
            log,
            timeProvider,
            Version);
}
