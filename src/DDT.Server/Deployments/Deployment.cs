// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;

namespace DDT.Server.Deployments;

// A run of a task sequence on one machine. The definition it runs and the files it downloads are frozen when it is
// assigned (DeploymentSnapshot, DeploymentStep, DeploymentArtifact), so editing the sequence never changes a run.
public sealed class Deployment
{
    public Guid Id { get; set; }

    public Guid MachineId { get; set; }

    // Cleared when the sequence is deleted; the run keeps its snapshot.
    public Guid? TaskSequenceId { get; set; }

    public long? SequenceRevision { get; set; }

    // The rule that chose the sequence, without a foreign key, so the history outlives the rule.
    public Guid? RuleId { get; set; }

    // The sequence's name when the run was assigned. A deployment from before task sequences keeps its image's name.
    public required string Title { get; set; }

    public int? DiskNumber { get; set; }

    public DeploymentState State { get; set; } = DeploymentState.Assigned;

    public DeploymentSource Source { get; set; }

    public Guid? RequestedByUserId { get; set; }

    public string? RequestedByName { get; set; }

    public int StepCount { get; set; }

    // The step the agent reported last, and its percent.
    public int? CurrentStepIndex { get; set; }

    public string? CurrentStepName { get; set; }

    public int Percent { get; set; }

    public SequencePhase? CurrentPhase { get; set; }

    public RunActivity? Activity { get; set; }

    // RunInputs as JSON, taken when the run started: the settings its answer file and domain join use. Never a secret.
    public string? Inputs { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset? StartedUtc { get; set; }

    public DateTimeOffset? FinishedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public string? Error { get; set; }
}
