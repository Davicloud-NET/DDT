// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Images;

public sealed record ImageSummary(
    Guid Id,
    string Name,
    ImageKind Kind,
    string Sha256,
    // For a raw disk image, the stored, compressed file.
    long SizeBytes,
    // 0 for a raw disk image.
    int WimIndex,
    string? Edition,
    string? Architecture,
    string? Version,
    string? Language,
    // For a raw disk image, the disk it holds.
    long InstalledBytes,
    string? OriginalFileName,
    DateTimeOffset UploadedUtc,
    string? UploadedBy,
    // For raw disk images only: whether it starts with Secure Boot on, and BootDetail why.
    ImageBootCapability? BootCapability = null,
    string? BootDetail = null,
    // Names a raw disk image's uncompressed disk, which is the same for an upload of that disk in another format.
    string? SourceSha256 = null);
