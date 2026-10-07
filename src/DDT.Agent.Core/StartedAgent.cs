// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;

namespace DDT.Agent;

// What the start-up made, for the system's own parts to build on. Graphical and ConsolePath are null when the agent
// runs without a graphical console.
public sealed record StartedAgent(
    AgentOptions Options,
    HttpAgentServer Server,
    AgentLog Log,
    ConsoleStatus Status,
    PipeMachineConsole? Graphical,
    string? ConsolePath,
    string Version);
