// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent;

// What the start-up takes from the executable, which is built for one system.
public sealed record AgentPlatform
{
    // The boot environment the executable is built for, which its sentences name.
    public required AgentEnvironment BootEnvironment { get; init; }

    // Only a published agent swaps itself for a newer one.
    public required bool IsPublished { get; init; }

    // Why this executable takes no newer agent or console from the server. Null for one that does.
    public string? NoUpdateReason { get; init; }

    // The executable's platform as AgentPlatforms names it, under which it asks for a newer agent. Null asks the
    // routes that agents in boot images have always asked, which answer for Windows.
    public string? ReleasePlatform { get; init; }

    // Whether the executable starts the graphical console beside it. One that does not keeps the text console.
    public bool StartsGraphicalConsole { get; init; } = true;

    // The executable carries the legal texts of what it is built from.
    public required Action<TextWriter> WriteStartupNotices { get; init; }

    public required Action<TextWriter> WriteLicenses { get; init; }

    // Each returns the agent's exit code.
    public required Func<StartedAgent, CancellationToken, Task<int>> DryRunAsync { get; init; }

    public required Func<StartedAgent, CancellationToken, Task<int>> RunAsync { get; init; }
}
