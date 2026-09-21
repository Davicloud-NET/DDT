// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Images;

public sealed record ImageSummary(
    Guid Id,
    string Name,
    ImageKind Kind,
    string Sha256,
    long SizeBytes,
    int WimIndex,
    string? Edition,
    string? Architecture,
    string? Version,
    string? Language,
    long InstalledBytes,
    string? OriginalFileName,
    DateTimeOffset UploadedUtc,
    string? UploadedBy);
