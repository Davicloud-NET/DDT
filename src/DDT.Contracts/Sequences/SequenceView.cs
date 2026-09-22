// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// StepPhases holds the phase each step runs in, in step order, as the engine decides it. Problems and Warnings are
// worked out on every read, because a deleted image or a changed setting changes them.
public sealed record SequenceView(
    Guid Id,
    string Name,
    string? Description,
    long Revision,
    SequenceDefinition Definition,
    IReadOnlyList<SequencePhase> StepPhases,
    IReadOnlyList<SequenceProblem> Problems,
    IReadOnlyList<SequenceProblem> Warnings,
    DateTimeOffset UpdatedUtc,
    string? UpdatedBy);
