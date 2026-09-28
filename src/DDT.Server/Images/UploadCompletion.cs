// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;
using DDT.Contracts.Messages;
using DDT.Contracts.Packages;

namespace DDT.Server.Images;

// For Added, Images holds the new entries. For Existing, it holds every entry of the file. For a package, Package is
// set instead. Offset is where an Incomplete upload continues, and Refusal says why the file can't be used.
public sealed record UploadCompletion(
    UploadCompletionStatus Status,
    IReadOnlyList<ImageSummary> Images,
    long Offset = 0,
    ServerMessage? Refusal = null,
    PackageSummary? Package = null);
