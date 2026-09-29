// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Server.Deployments;

// One step of a run. It's created Pending when the run is assigned, and the agent's reports move it on. The times are
// the server's, because the WinPE clock can be hours off.
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

    // A run of a tree has a row per node, containers included, and Index is the node's position in pre-order. ParentId
    // is the node's container, null at the top level, and Depth counts containers from 0.
    public Guid? ParentId { get; set; }

    public int Depth { get; set; }

    // The node's latest visit, as StepRunState has it. A run from before trees, or of an agent that sends no passes, has
    // Pass 0 on every row.
    public int Pass { get; set; }

    public int Iteration { get; set; }

    public IfBranch? Branch { get; set; }

    // The node's tests as they were decided, as a list of TestEvaluation written by DdtJsonContext. Null for none.
    public string? Evaluation { get; set; }
}
