// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// What SequenceValidator finds. Problems keep a sequence from running. Warnings don't.
public sealed record SequenceAnalysis(
    IReadOnlyList<SequenceProblem> Problems,
    IReadOnlyList<SequenceProblem> Warnings,
    // One entry per node in SequenceTree.Nodes order, each with Windows PE listed first. A container runs in the phase
    // it starts in and in the phases of its nodes.
    IReadOnlyList<NodePhase> NodePhases,
    // Names used by conditions and templates that only rules and machine roles can set. The server knows those
    // values, the agent doesn't.
    IReadOnlyList<string> ValueNames);
