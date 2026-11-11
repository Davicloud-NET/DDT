// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Applications;

namespace DDT.Agent.Applications;

// Asks Windows Installer. Advertised products and those installed for another user only count as missing.
public sealed class MsiInstalledProducts : IInstalledProducts
{
    public bool IsInstalled(string productCode) =>
        ApplicationDetectionRules.NormalizedProductCode(productCode) is { } code
        && MsiNativeMethods.MsiQueryProductState(code) == MsiNativeMethods.InstallStateDefault;
}
