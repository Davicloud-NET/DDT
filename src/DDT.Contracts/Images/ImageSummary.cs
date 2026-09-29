// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Images;

public sealed record ImageSummary(
    Guid Id,
    string Name,
    ImageKind Kind,
    string Sha256,
    // For a raw disk image, the size of the stored, compressed file.
    long SizeBytes,
    // 0 for a raw disk image.
    int WimIndex,
    string? Edition,
    string? Architecture,
    string? Version,
    string? Language,
    // For a raw disk image, the size of the disk it holds.
    long InstalledBytes,
    string? OriginalFileName,
    DateTimeOffset UploadedUtc,
    string? UploadedBy,
    // For raw disk images only. Whether the image boots with Secure Boot on, and BootDetail says why.
    ImageBootCapability? BootCapability = null,
    string? BootDetail = null,
    // The hash of a raw disk image's uncompressed disk. It's the same when that disk is uploaded in another format.
    string? SourceSha256 = null);
