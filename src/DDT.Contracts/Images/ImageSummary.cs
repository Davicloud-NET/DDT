// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Images;

// For a raw disk image, SizeBytes is the stored, compressed file and InstalledBytes the disk it holds, WimIndex is 0,
// and SourceSha256 names the uncompressed disk, which an upload of the same disk in another format also is.
// BootCapability says whether it starts with Secure Boot on, and BootDetail why, for raw disk images only.
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
    string? UploadedBy,
    ImageBootCapability? BootCapability = null,
    string? BootDetail = null,
    string? SourceSha256 = null);
