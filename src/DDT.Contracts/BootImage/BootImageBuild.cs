// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.BootImage;

// The boot image in the boot directory, as Build-BootImage.ps1 described it in ddt-boot-image.json next to boot.wim.
// DriverSetHash is computed from Drivers the way BootImageView's is, null when the build added none of DDT's packages.
// AdkVersion is the Windows PE add-on's, BootManager the version of the boot managers it published, and AgentVersion
// that of the agent it put in, each null when the script could not tell.
public sealed record BootImageBuild(
    DateTimeOffset BuiltUtc,
    string? DriverSetHash,
    IReadOnlyList<BootImageBuildDriver> Drivers,
    string? AdkVersion,
    string? BootManager,
    string? AgentVersion);
