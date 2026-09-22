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
}
