// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;

namespace DDT.Server.Deployments;

// The result of an answer or a continue on the machine's page. View is the current run. Changed is true if this request
// changed it, and false if the request came too late. Invalid holds the answers' problems. With none of them set, the
// machine has no run.
internal sealed record WaitingRunOutcome(DeploymentView? View, bool Changed, DeploymentDecision? Invalid)
{
    public static WaitingRunOutcome NotFound { get; } = new(null, false, null);
}
