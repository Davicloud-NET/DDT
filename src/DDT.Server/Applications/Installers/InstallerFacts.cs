// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Applications.Installers;

// What an uploaded installer says about itself. ProductCode is an MSI's; BundleCode is the GUID a WiX Burn bundle
// registers its uninstall key under; UninstallKey is the key an Inno Setup or NSIS installer writes.
public sealed record InstallerFacts(
    InstallerKind Kind,
    string? Name = null,
    string? Publisher = null,
    string? Version = null,
    string? ProductCode = null,
    string? UpgradeCode = null,
    string? BundleCode = null,
    string? UninstallKey = null)
{
    public static readonly InstallerFacts Unknown = new(InstallerKind.Unknown);
}
