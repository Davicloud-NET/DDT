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
    public static AgentRunReport Report(
        DeploymentState state,
        IReadOnlyList<StepRunState> steps,
        int percent = 0,
        string? error = null,
        SequencePhase phase = SequencePhase.WindowsPE,
        RunActivity activity = RunActivity.Step) =>
        new(state, phase, steps, steps.LastOrDefault(s => s.State == StepState.Running)?.StepId, percent, activity, error);

    public static AgentRunReport Running(params StepRunState[] steps) => Report(DeploymentState.Running, steps);

    public static StepRunState Step(SequenceStep step, StepState state, string? error = null) => new(step.Id, state, error);
}
