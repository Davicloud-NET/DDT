// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

public sealed record SequenceView(
    Guid Id,
    string Name,
    string? Description,
    long Revision,
    SequenceDefinition Definition,
    // The phase each step runs in, in step order, as the engine decides it.
    IReadOnlyList<SequencePhase> StepPhases,
    // Problems and Warnings are worked out on every read, because a deleted image or a changed setting changes them.
    IReadOnlyList<SequenceProblem> Problems,
    IReadOnlyList<SequenceProblem> Warnings,
    DateTimeOffset UpdatedUtc,
    string? UpdatedBy,
    // The phases each node may run in, in the order of SequenceTree.Nodes: more than one where it depends on the path
    // an IF takes.
    IReadOnlyList<NodePhase>? NodePhases = null);
