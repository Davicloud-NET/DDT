// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// What SequencePaths finds at the nodes it walks, by how much it matters.
internal sealed class SequenceFindings
{
    public List<SequenceProblem> Problems { get; } = [];

    public List<SequenceProblem> Warnings { get; } = [];

    public List<SequenceProblem> Notes { get; } = [];
}
