// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Applications;

// Installed when HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall has this key, in the 64-bit or 32-bit view.
public sealed record UninstallKeyDetection(string KeyName) : ApplicationDetection;
