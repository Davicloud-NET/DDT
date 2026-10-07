// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// What the start-up takes from the executable, which is built for one system.
public sealed record AgentPlatform
{
    // Only a published agent swaps itself for a newer one.
    public required bool IsPublished { get; init; }

    // The executable carries the legal texts of what it is built from.
    public required Action<TextWriter> WriteStartupNotices { get; init; }

    public required Action<TextWriter> WriteLicenses { get; init; }

    // Each returns the agent's exit code.
    public required Func<StartedAgent, CancellationToken, Task<int>> DryRunAsync { get; init; }

    public required Func<StartedAgent, CancellationToken, Task<int>> RunAsync { get; init; }
}
