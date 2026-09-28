// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Packages;

namespace DDT.Server.Packages;

// A package is a zip in the content store, next to the images. Files are shared by content. The same zip uploaded as
// the other kind becomes a second package that uses the same file.
public sealed class Package
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public PackageKind Kind { get; set; }

    public required string Sha256 { get; set; }

    public long SizeBytes { get; set; }

    public long ExpandedBytes { get; set; }

    public int FileCount { get; set; }

    // Holds a JSON array of HardwareModel, as PackageTargets writes it. A few dozen packages are matched in memory, so
    // the targets don't need their own table.
    public string Targets { get; set; } = "[]";

    public string? Description { get; set; }

    public string? OriginalFileName { get; set; }

    public DateTimeOffset UploadedUtc { get; set; }

    public Guid? UploadedByUserId { get; set; }

    // Marks a driver package that Build-BootImage.ps1 adds to the WinPE boot image. It's for a network or storage
    // controller that WinPE has no driver for. Machines need it before any sequence runs, so every machine that boots
    // the image gets it, whatever the targets say.
    public bool BootImage { get; set; }

    public string? UploadedByName { get; set; }
}
