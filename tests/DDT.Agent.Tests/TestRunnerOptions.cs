// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// What a test changes of the runner TestAgents.Runner builds; null keeps the fake's. Without a heartbeat interval,
// beats happen only when the run changes, so a run stays sequential on ImmediateTimeProvider.
internal sealed record TestRunnerOptions
{
    public TimeSpan? HeartbeatInterval { get; init; }

    public IToolRunner? ToolRunner { get; init; }

    public IBcdWriter? BcdWriter { get; init; }

    public IRebooter? Rebooter { get; init; }

    public IDomainJoiner? Joiner { get; init; }

    public IRawDisks? RawDisks { get; init; }

    public string? SystemDirectory { get; init; }

    // A dry hand-over only logs the registration of the service.
    public bool DryRunHandOver { get; init; }

    public bool DryRun { get; init; } = true;

    public ConsoleStatus? Status { get; init; }

    public string? ConsoleDirectory { get; init; }
}
