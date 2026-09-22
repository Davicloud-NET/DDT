// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Contracts.Deployments;

// One step of a run as the agent last reported it. Kind is the step's kind as the sequence document names it. The
// times are the server's, taken when a report showed the step start and end.
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
    string? Error);
