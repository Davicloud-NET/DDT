// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Packages;

namespace DDT.Server.Packages;

// A zip in the content store, next to the images. Its file is shared by content: the same zip uploaded as the other
// kind is a second package of the same file.
public sealed class Package
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public PackageKind Kind { get; set; }

    public required string Sha256 { get; set; }

    public long SizeBytes { get; set; }

    public long ExpandedBytes { get; set; }

    public int FileCount { get; set; }

    // A JSON array of HardwareModel as PackageTargets writes it. A few dozen packages are matched in memory, so the
    // targets need no table of their own.
    public string Targets { get; set; } = "[]";

    public string? Description { get; set; }

    public string? OriginalFileName { get; set; }

    public DateTimeOffset UploadedUtc { get; set; }

    public Guid? UploadedByUserId { get; set; }

    // A driver package that Build-BootImage.ps1 adds to the Windows PE boot image, for a network or storage controller
    // Windows PE has no driver for. A machine needs it before any sequence runs, so it goes to every machine that boots
    // the image, whatever the package's targets say about the installed Windows.
    public bool BootImage { get; set; }

    public string? UploadedByName { get; set; }
}
