// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;

namespace DDT.Agent.Tests;

// What a test changes about the runner TestAgents.Runner or WindowsTestAgents.Runner builds. Null keeps the fake's
// default. Without a heartbeat interval, beats only happen when the run changes, so a run stays sequential on
// ImmediateTimeProvider.
internal sealed record TestRunnerOptions
{
    public TimeSpan? HeartbeatInterval { get; init; }

    public IToolRunner? ToolRunner { get; init; }

    public IBcdWriter? BcdWriter { get; init; }

    public IRebooter? Rebooter { get; init; }

    public IDomainJoiner? Joiner { get; init; }

    public IRawDisks? RawDisks { get; init; }

    // The machine's disks, where a test brings those of a system. Null is the fake's.
    public IDisks? Disks { get; init; }

    // The boot environment the runner runs in, whose check TestAgents.Runner makes of a fresh run. Null is Windows PE
    // without the check.
    public AgentEnvironment? Environment { get; init; }

    // The hand-over of a system. Null is one that only goes into the tools' journal.
    public IHandOver? HandOver { get; init; }

    // What the script launcher adds to every script's environment.
    public IReadOnlyDictionary<string, string>? ScriptEnvironment { get; init; }

    public string? SystemDirectory { get; init; }

    // A dry hand-over only logs the registration of the service.
    public bool DryRunHandOver { get; init; }

    public bool DryRun { get; init; } = true;

    public ConsoleStatus? Status { get; init; }

    public string? ConsoleDirectory { get; init; }
}
