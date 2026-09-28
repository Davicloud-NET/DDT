// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;
using DDT.Server.Deployments;

namespace DDT.Server.Machines;

// What a decision about a machine came to: its row once saved, or the refusal. Neither when the machine is gone.
internal sealed record MachineOutcome(MachineSummary? Summary, DeploymentDecision? Refusal)
{
    public static MachineOutcome NotFound { get; } = new(null, null);

    public static MachineOutcome Saved(MachineSummary summary) => new(summary, null);

    public static MachineOutcome Refused(DeploymentDecision refusal) => new(null, refusal);
}
