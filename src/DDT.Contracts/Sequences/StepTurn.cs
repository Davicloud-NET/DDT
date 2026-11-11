// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// What a step kept for its visit. An entry whose Pass is not the node's current pass is from an earlier visit and
// counts for nothing.
public sealed record StepTurn(Guid StepId, int Pass, bool RestartDue, string? Progress);
