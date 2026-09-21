// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;

namespace DDT.Server.Images;

// Images holds the new library entries for Added and every entry of the file's hash for Existing. Offset is where
// an Incomplete upload continues. Refusal is the sentence that says why the file cannot be used.
public sealed record UploadCompletion(
    UploadCompletionStatus Status,
    IReadOnlyList<ImageSummary> Images,
    long Offset = 0,
    string? Refusal = null);
