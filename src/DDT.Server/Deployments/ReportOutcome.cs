// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Server.Deployments;

// The result of an agent's report: the agent's answer, or the refusal. Unauthorized means the machine started over
// after the token was checked. With none of them set, the machine doesn't exist.
internal sealed record ReportOutcome(AgentRunReportResult? Result, DeploymentDecision? Refusal, bool Unauthorized = false)
{
    public static ReportOutcome NotFound { get; } = new(null, null);
}
