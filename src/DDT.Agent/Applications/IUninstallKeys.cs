// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Applications;

// The keys under SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall, which Programs and Features lists.
public interface IUninstallKeys
{
    bool Exists(string keyName);
}
