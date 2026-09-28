// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Contracts.Deployments;

// One step of a run, as the agent last reported it. A run of a tree has one per node, including containers. The
// times are the server's, taken from the reports that showed the step start and end.
public sealed record DeploymentStepView(
    Guid StepId,
    // The node's place in pre-order.
    int Index,
    string Name,
    // The step's kind as the sequence document names it.
    string Kind,
    SequencePhase Phase,
    StepState State,
    int Percent,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? FinishedUtc,
    string? Error,
    // The container the step sits in, null at the top.
    Guid? ParentId = null,
    // The nesting depth, counting containers from 0.
    int Depth = 0,
    // Pass, Iteration, Branch and Evaluation describe the node's latest visit, as in StepRunState. Earlier visits are
    // in the log.
    int Pass = 0,
    int Iteration = 0,
    IfBranch? Branch = null,
    IReadOnlyList<TestEvaluation>? Evaluation = null);
