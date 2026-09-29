// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Audit;

// Newest first. Next is the id to pass as the before parameter to get the next page. It's null on the last page.
public sealed record AuditPage(IReadOnlyList<AuditEntry> Items, long? Next);
