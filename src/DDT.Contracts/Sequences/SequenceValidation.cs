// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Problems keep a sequence from running; warnings do not.
public sealed record SequenceValidation(IReadOnlyList<SequenceProblem> Problems, IReadOnlyList<SequenceProblem> Warnings);
