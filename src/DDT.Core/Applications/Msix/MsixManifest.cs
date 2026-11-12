// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Applications.Msix;

// What a package's AppxManifest.xml or a bundle's AppxBundleManifest.xml says about it, a bundle's names from its first
// application package. DisplayName falls back to the identity name, PublisherDisplayName to the CN of Publisher.
public sealed record MsixManifest(
    string Name,
    string Publisher,
    string Version,
    string? ProcessorArchitecture,
    string DisplayName,
    string? PublisherDisplayName,
    bool IsFramework,
    bool IsBundle)
{
    public string FamilyName => PackageFamilyNames.Of(Name, Publisher);
}
