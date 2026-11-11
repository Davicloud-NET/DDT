// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Applications;

// The MSI products Windows Installer knows.
public interface IInstalledProducts
{
    // productCode is a GUID in any of its usual forms.
    bool IsInstalled(string productCode);
}
