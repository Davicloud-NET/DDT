// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.BootImage;

// The boot image in the boot directory, as Build-BootImage.ps1 described it in ddt-boot-image.json next to boot.wim.
public sealed record BootImageBuild(
    DateTimeOffset BuiltUtc,
    // Computed from Drivers the same way as BootImageView.DriverSetHash. Null when the build added none of DDT's
    // packages.
    string? DriverSetHash,
    IReadOnlyList<BootImageBuildDriver> Drivers,
    // The version of the Windows PE add-on. This, BootManager and AgentVersion are null when the script couldn't
    // find them out.
    string? AdkVersion,
    // The version of the boot managers the build published.
    string? BootManager,
    string? AgentVersion,
    // What the image was built for: the address and root the agent in it uses, its keyboard layout and whether it has
    // PowerShell. Null from a build script that did not record them yet.
    string? ServerUrl = null,
    string? RootSha256 = null,
    string? KeyboardLayout = null,
    bool? PowerShell = null);
