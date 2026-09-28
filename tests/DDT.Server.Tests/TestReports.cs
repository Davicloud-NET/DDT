// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;

namespace DDT.Server.Tests;

// Reports as the agent sends them: every step that has left Pending, and the one running now as the current step.
internal static class TestReports
{
    // A report from Windows PE of a step, whose current step is the last one running. A test sets the rest with a with
    // expression.
    public static AgentRunReport Report(DeploymentState state, IReadOnlyList<StepRunState> steps) =>
        new(state, SequencePhase.WindowsPE, steps, steps.LastOrDefault(s => s.State == StepState.Running)?.StepId, 0, RunActivity.Step, null);

    public static AgentRunReport Running(params StepRunState[] steps) => Report(DeploymentState.Running, steps);

    public static StepRunState Step(SequenceStep step, StepState state, string? error = null) => new(step.Id, state, error);
}
