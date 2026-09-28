// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Contracts.Deployments;

// One step of a run as the agent last reported it. Kind is the step's kind as the sequence document names it. The
// times are the server's, taken when a report showed the step start and end.
//
// A run of a tree has a step per node, containers included, and Index is the node's place in pre-order. ParentId is the
// container it sits in, null at the top, and Depth counts containers from 0. Pass, Iteration, Branch and Evaluation
// are the node's latest visit as StepRunState has them; earlier visits are in the log.
public sealed record DeploymentStepView(
    Guid StepId,
    int Index,
    string Name,
    string Kind,
    SequencePhase Phase,
    StepState State,
    int Percent,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? FinishedUtc,
    string? Error,
    Guid? ParentId = null,
    int Depth = 0,
    int Pass = 0,
    int Iteration = 0,
    IfBranch? Branch = null,
    IReadOnlyList<TestEvaluation>? Evaluation = null);
