// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// Whether a condition held, and how each of its tests turned out, for StepRunState.Evaluation. The tests are in the
// order they're written, at most TestEvaluation.MaxPerNode of them.
public sealed record ConditionResult(bool Held, IReadOnlyList<TestEvaluation> Evaluations);
