// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.BootImage;

// Maps ddt-boot-image.json, which Build-BootImage.ps1 writes next to boot.wim. Everything is optional here, because the
// file comes from outside the server. BootImageCatalog decides what makes it readable.
internal sealed class BootImageManifest
{
    public DateTimeOffset? BuiltUtc { get; set; }

    public string? DriverSetHash { get; set; }

    public List<BootImageManifestDriver?>? Drivers { get; set; }

    public string? AdkVersion { get; set; }

    public string? BootManager { get; set; }

    public string? AgentVersion { get; set; }

    public string? ServerUrl { get; set; }

    public string? RootSha256 { get; set; }

    public string? KeyboardLayout { get; set; }

    public bool? PowerShell { get; set; }
}
