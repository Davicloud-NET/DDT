// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Server.Packages;

// Refusal is the sentence that says why the zip cannot be a package, and RefusalMessage the same as a code; the counts
// are then zero.
public sealed record PackageInspection(int FileCount, long ExpandedBytes, string? Refusal, ServerMessage? RefusalMessage = null);
