// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// Error is set when the run failed.
public sealed record SequenceRunResult(SequenceOutcome Outcome, SequenceState State, string? Error);
