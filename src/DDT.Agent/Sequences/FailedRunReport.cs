// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;

namespace DDT.Agent.Sequences;

// The report of a run that failed where the agent knows none of its steps: before it ran, or once its state is gone.
public static class FailedRunReport
{
    public static AgentRunReport Of(SequencePhase phase, string error) =>
        new(DeploymentState.Failed, phase, [], null, 0, RunActivity.Preparing, error);
}
