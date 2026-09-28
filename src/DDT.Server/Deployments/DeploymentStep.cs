// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Server.Deployments;

// One step of a run, created Pending when the run is assigned and moved on by the agent's reports. The times are the
// server's: the Windows PE clock can be hours off.
public sealed class DeploymentStep
{
    public Guid DeploymentId { get; set; }

    public Guid StepId { get; set; }

    public int Index { get; set; }

    public required string Name { get; set; }

    // The step's kind as the sequence document names it, such as applyImage.
    public required string Kind { get; set; }

    public SequencePhase Phase { get; set; }

    public StepState State { get; set; }

    public int Percent { get; set; }

    public DateTimeOffset? StartedUtc { get; set; }

    public DateTimeOffset? FinishedUtc { get; set; }

    public string? Error { get; set; }

    // A run of a tree has a row per node, containers included, and Index is the node's place in pre-order. ParentId is
    // the container the node sits in, null at the top, and Depth counts containers from 0.
    public Guid? ParentId { get; set; }

    public int Depth { get; set; }

    // The node's latest visit, as StepRunState has it. A run from before trees, or of an agent that sends no passes, has
    // Pass 0 on every row.
    public int Pass { get; set; }

    public int Iteration { get; set; }

    public IfBranch? Branch { get; set; }

    // The node's tests as they were decided, as DdtJsonContext writes a list of TestEvaluation; null for none.
    public string? Evaluation { get; set; }
}
