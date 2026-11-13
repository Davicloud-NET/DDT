// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Contracts.Import;

// What became of one file, driver group, application or bundle. Reason says why it was refused, and ReasonText is the
// same in English. Notes are what an imported application lost on the way, such as a dependency.
public sealed record ImportResult(
    string Name,
    ImportOutcome Outcome,
    ServerMessage? Reason = null,
    string? ReasonText = null,
    IReadOnlyList<ServerMessage>? Notes = null);
