// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Applications;

// Installed when a package of this family, such as Microsoft.DesktopAppInstaller_8wekyb3d8bbwe, is provisioned for all
// users.
public sealed record PackageFamilyDetection(string FamilyName) : ApplicationDetection;
