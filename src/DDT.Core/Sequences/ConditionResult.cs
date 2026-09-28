// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// Whether a condition held, and each of its tests as it was decided, in the order they are written and at most
// TestEvaluation.MaxPerNode of them, for StepRunState.Evaluation.
public sealed record ConditionResult(bool Held, IReadOnlyList<TestEvaluation> Evaluations);
