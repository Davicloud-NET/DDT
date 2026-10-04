// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Import;

// An import as the Images page follows it. Item is what is being read, and DoneBytes of TotalBytes how far that is.
public sealed record ImportStatus(
    DateTimeOffset StartedUtc,
    string StartedBy,
    ImportState State,
    DateTimeOffset? FinishedUtc,
    string? Item,
    long DoneBytes,
    long TotalBytes,
    int Items,
    IReadOnlyList<ImportResult> Results);
