// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Sequences;

// WorkDirectory keeps the run's files until Partition gives the run a directory on disk. In a dry run it's under the
// dry run's root.
public sealed record SequenceRunnerOptions(TimeSpan HeartbeatInterval, string WorkDirectory, bool DryRun)
{
    // The boot environment or installed system the runner runs in, which its sentences name.
    public AgentEnvironment Environment { get; init; } = AgentEnvironment.WindowsPE;
}
