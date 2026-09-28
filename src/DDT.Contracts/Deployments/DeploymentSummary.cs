// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;

namespace DDT.Contracts.Deployments;

// A run of a task sequence.
public sealed record DeploymentSummary(
    Guid Id,
    Guid? SequenceId,
    // The sequence's name when it was assigned. A deployment that predates task sequences has no steps and uses the
    // image's name.
    string Title,
    DeploymentState State,
    DeploymentSource Source,
    string? RequestedBy,
    int StepCount,
    // Counts from 0. This, StepName, Percent and Phase describe the step the agent reported last.
    int? StepIndex,
    string? StepName,
    int Percent,
    SequencePhase? Phase,
    RunActivity? Activity,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? FinishedUtc,
    DateTimeOffset UpdatedUtc,
    string? Error,
    // The run needs someone to answer its inputs or to continue a pause.
    bool Waiting = false,
    string? PauseMessage = null);
