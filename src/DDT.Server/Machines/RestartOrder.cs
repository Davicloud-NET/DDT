// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Server.Machines;

// What a poll tells a machine whose assigned run starts in the other boot environment: to restart into it, or why the
// run waits. Changed: the machine or the run was changed and has to be saved before the answer goes out.
internal sealed record RestartOrder(AgentEnvironment? Into, string? WaitReason, bool Changed)
{
    public static RestartOrder None { get; } = new(null, null, false);
}
