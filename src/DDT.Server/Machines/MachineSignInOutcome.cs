// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Server.Machines;

// The result of a sign-in at the machine. With neither a status nor Unauthorized, the machine doesn't exist.
// Unauthorized means the machine started over after the token was checked.
internal sealed record MachineSignInOutcome(AgentSignInStatus? Status, bool Unauthorized)
{
    public static MachineSignInOutcome NotFound { get; } = new(null, false);

    public static MachineSignInOutcome StartedOver { get; } = new(null, true);

    public static MachineSignInOutcome Answered(AgentSignInStatus status) => new(status, false);
}
