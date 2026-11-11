// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Win32;

namespace DDT.Agent.Applications;

// A 32-bit installer writes its key into the 32-bit view, so both views are looked at. A per-user installer, such as
// Python's Burn bundle, writes into HKCU, which for the agent is SYSTEM's own.
public sealed class RegistryUninstallKeys(string uninstallPath = RegistryUninstallKeys.UninstallPath) : IUninstallKeys
{
    public const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public bool Exists(string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        foreach (RegistryHive hive in (RegistryHive[])[RegistryHive.LocalMachine, RegistryHive.CurrentUser])
        {
            foreach (RegistryView view in (RegistryView[])[RegistryView.Registry64, RegistryView.Registry32])
            {
                using RegistryKey root = RegistryKey.OpenBaseKey(hive, view);
                using RegistryKey? key = root.OpenSubKey($@"{uninstallPath}\{keyName}");

                if (key is not null)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
