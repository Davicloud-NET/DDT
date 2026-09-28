// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// What SequenceValidator finds. Problems keep a sequence from running; warnings do not.
public sealed record SequenceAnalysis(
    IReadOnlyList<SequenceProblem> Problems,
    IReadOnlyList<SequenceProblem> Warnings,
    // In the order of SequenceTree.Nodes, Windows PE first. A container runs in the phases of its start and its nodes.
    IReadOnlyList<NodePhase> NodePhases,
    // Names conditions and templates use that only rules and machine roles can set, which the server knows, not the
    // agent.
    IReadOnlyList<string> ValueNames);
