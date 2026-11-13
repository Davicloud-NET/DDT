// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Applications;

// The full names of the packages provisioned for all users, such as
// Microsoft.DesktopAppInstaller_2026.917.151.0_neutral_~_8wekyb3d8bbwe.
public interface IProvisionedPackages
{
    IReadOnlyList<string> FullNames();
}
