// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;

namespace DDT.Contracts.Packages;

public sealed record PackageSummary(
    Guid Id,
    string Name,
    PackageKind Kind,
    string Sha256,
    long SizeBytes,
    // The space the files need once unpacked. The server checks it by unpacking them without writing them to disk.
    long ExpandedBytes,
    int FileCount,
    IReadOnlyList<HardwareModel> Targets,
    string? Description,
    string? OriginalFileName,
    DateTimeOffset UploadedUtc,
    string? UploadedBy,
    // Set on a driver package that goes into the Windows PE boot image.
    bool BootImage = false);
