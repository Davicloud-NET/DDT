// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Server.Machines;

// What a poll answers about a run of the other boot environment: restart into it, or why not. Changed: save first.
internal sealed record RestartOrder(AgentEnvironment? Into, string? WaitReason, bool Changed)
{
    public static RestartOrder None { get; } = new(null, null, false);
}
