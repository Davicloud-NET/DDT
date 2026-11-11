// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;

namespace DDT.Agent.Applications;

public static class ApplicationDetections
{
    // What the rule looks for, as the log names it.
    public static string Describe(ApplicationDetection detection) => detection switch
    {
        ProductCodeDetection product => $"the product code {product.ProductCode}",
        UninstallKeyDetection key => $"the uninstall key {key.KeyName}",
        FileDetection { MinimumVersion: { Length: > 0 } minimum } file => $"{file.Path} at version {minimum} or later",
        FileDetection file => file.Path,
        PackageFamilyDetection { MinimumVersion: { Length: > 0 } minimum } family => $"the provisioned package family {family.FamilyName} at {minimum} or later",
        PackageFamilyDetection family => $"the provisioned package family {family.FamilyName}",
        _ => "its detection rule",
    };
}
