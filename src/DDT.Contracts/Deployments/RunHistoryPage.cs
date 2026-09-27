// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Deployments;

// Next is the cursor for the page after this one, null on the last page. Counts come with the first page only.
public sealed record RunHistoryPage(IReadOnlyList<RunHistoryItem> Items, string? Next, RunStateCounts? Counts);
