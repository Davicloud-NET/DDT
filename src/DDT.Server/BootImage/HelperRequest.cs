// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.BootImage;

// What the server asks the DDT Helper service to do as SYSTEM. It carries values, never a path or a command: the
// helper derives every path itself and runs the one script that came with the release.
public sealed record HelperRequest
{
    public const string Build = "build";
    public const string InstallAdk = "adk";

    public required string Kind { get; init; }

    // The folder name of the new build below the boot directory.
    public string? Name { get; init; }

    public string? ServerUrl { get; init; }

    public string? KeyboardLayout { get; init; }

    public bool SkipPowerShell { get; init; }

    public int? TftpWindowSize { get; init; }

    // The driver packages flagged for the boot image. The helper takes each from the store by its hash.
    public IReadOnlyList<HelperDriver> Drivers { get; init; } = [];

    public string? DriverSetHash { get; init; }
}
