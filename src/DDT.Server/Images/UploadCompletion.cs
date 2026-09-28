// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;
using DDT.Contracts.Messages;
using DDT.Contracts.Packages;

namespace DDT.Server.Images;

// Images holds the new entries for Added and every entry of the file for Existing; Package is set instead for a
// package. Offset is where an Incomplete upload continues, and Refusal says why the file cannot be used.
public sealed record UploadCompletion(
    UploadCompletionStatus Status,
    IReadOnlyList<ImageSummary> Images,
    long Offset = 0,
    ServerMessage? Refusal = null,
    PackageSummary? Package = null);
