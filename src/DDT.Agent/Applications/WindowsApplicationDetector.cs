// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Core.Applications.Msix;

namespace DDT.Agent.Applications;

// Looks for an application the way its detection rule says: the MSI product, the uninstall key, the file, or the
// provisioned package family.
public sealed class WindowsApplicationDetector(
    IInstalledProducts products,
    IUninstallKeys uninstallKeys,
    IDetectionFiles files,
    IProvisionedPackages packages) : IApplicationDetector
{
    public static WindowsApplicationDetector ForThisMachine() =>
        new(new MsiInstalledProducts(), new RegistryUninstallKeys(), new SystemDetectionFiles(), new RegistryProvisionedPackages());

    public bool IsInstalled(ApplicationDetection detection) => detection switch
    {
        ProductCodeDetection product => products.IsInstalled(product.ProductCode),
        UninstallKeyDetection key => uninstallKeys.Exists(key.KeyName.Trim()),
        FileDetection file => HasFile(file),
        PackageFamilyDetection family => packages.FullNames().Any(fullName => PackageFamilyNames.IsOfFamily(fullName, family.FamilyName.Trim())),
        _ => false,
    };

    // A file without a version is not new enough for a rule that asks for one.
    private bool HasFile(FileDetection rule)
    {
        string path = files.Expand(rule.Path.Trim());

        if (!files.Exists(path))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(rule.MinimumVersion)
            || (files.VersionOf(path) is { } version && FileVersions.IsAtLeast(version, rule.MinimumVersion));
    }
}
