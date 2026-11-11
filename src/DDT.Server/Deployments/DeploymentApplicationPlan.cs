// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

// What an Install applications step of a run installs: frozen when the run is created, added to when it starts, so later
// library edits never reach the run.
public sealed class DeploymentApplicationPlan
{
    public Guid DeploymentId { get; set; }

    public Guid StepId { get; set; }

    // The AgentApplication list in install order, as RunApplicationPlans writes it.
    public required string Applications { get; set; }
}
