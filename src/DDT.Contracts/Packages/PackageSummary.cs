// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;

namespace DDT.Contracts.Packages;

// ExpandedBytes is what the files take once unpacked, checked by unpacking them on the server without writing them.
// BootImage is set on a driver package that goes into the Windows PE boot image, see BootImageView.
public sealed record PackageSummary(
    Guid Id,
    string Name,
    PackageKind Kind,
    string Sha256,
    long SizeBytes,
    long ExpandedBytes,
    int FileCount,
    IReadOnlyList<HardwareModel> Targets,
    string? Description,
    string? OriginalFileName,
    DateTimeOffset UploadedUtc,
    string? UploadedBy,
    bool BootImage = false);
