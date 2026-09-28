// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;

namespace DDT.Agent.Tests;

// What a test changes of the run SequenceRunnerTestBase starts; null keeps the default. Resumed is the run's state found
// on the disk, and ConfirmedDisk the disk confirmed with ERASE.
internal sealed record SequenceRunOptions
{
    public LocalRun? Resumed { get; init; }

    public LocalDisk? ConfirmedDisk { get; init; }

    public TimeProvider? Time { get; init; }

    public TimeSpan? HeartbeatInterval { get; init; }

    public AgentLog? Log { get; init; }

    public IRebooter? Rebooter { get; init; }

    public string? RunToken { get; init; }

    public IToolRunner? ToolRunner { get; init; }

    public bool DryRunHandOver { get; init; }

    public string? ConsoleDirectory { get; init; }
}
