// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Contracts.Import;

// What became of one file or driver group. Reason says why it was refused, and ReasonText is the same in English.
public sealed record ImportResult(string Name, ImportOutcome Outcome, ServerMessage? Reason = null, string? ReasonText = null);
