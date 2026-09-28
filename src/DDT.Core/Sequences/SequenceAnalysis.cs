// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// What SequenceValidator finds: the problems that keep a sequence from running, the warnings that do not, and the
// phases each node may run in, in the order of SequenceTree.Nodes with Windows PE first. A container runs in the
// phases of its start and of every node inside it. ValueNames are the names its conditions test that neither the
// machine nor the sequence gives a value, in the order they first appear: only rules and machine roles can, which the
// server knows and the agent does not.
public sealed record SequenceAnalysis(
    IReadOnlyList<SequenceProblem> Problems,
    IReadOnlyList<SequenceProblem> Warnings,
    IReadOnlyList<NodePhase> NodePhases,
    IReadOnlyList<string> ValueNames);
