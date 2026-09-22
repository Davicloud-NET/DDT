// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;

namespace DDT.Contracts.Deployments;

// A run of a task sequence. Title is the sequence's name when it was assigned, or the image's for a deployment from
// before task sequences, which has no steps. StepIndex counts from 0; it, StepName, Percent and Phase are the step the
// agent reported last, and Activity is what the agent does between steps. UpdatedUtc is the last change.
public sealed record DeploymentSummary(
    Guid Id,
    Guid? SequenceId,
    string Title,
    DeploymentState State,
    DeploymentSource Source,
    string? RequestedBy,
    int StepCount,
    int? StepIndex,
    string? StepName,
    int Percent,
    SequencePhase? Phase,
    RunActivity? Activity,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? FinishedUtc,
    DateTimeOffset UpdatedUtc,
    string? Error);
