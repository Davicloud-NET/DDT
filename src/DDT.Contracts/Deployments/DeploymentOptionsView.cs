// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Contracts.Deployments;

// What the web UI needs to explain what an assignment does before it's sent. With ZeroTouchEnabled, web approval is
// off, and an assignment to a machine that isn't waiting carries over to its next netboot from a listed network.
// ServerUtc lets the browser correct its clock, because the server uses its own clock to decide whether a machine
// is still at its prompt.
public sealed record DeploymentOptionsView(
    bool DomainConfigured,
    bool RequireWebApproval,
    bool ZeroTouchEnabled,
    DateTimeOffset ServerUtc,
    // The boot environments this server can start a machine in. A sequence of the other one makes a machine restart.
    IReadOnlyList<AgentEnvironment>? BootEnvironments = null);
