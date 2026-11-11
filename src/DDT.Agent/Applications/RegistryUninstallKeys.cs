// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Win32;

namespace DDT.Agent.Applications;

// A 32-bit installer writes its key into the 32-bit view, so both views are looked at.
public sealed class RegistryUninstallKeys : IUninstallKeys
{
    public const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public bool Exists(string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        foreach (RegistryView view in (RegistryView[])[RegistryView.Registry64, RegistryView.Registry32])
        {
            using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? key = machine.OpenSubKey($@"{UninstallPath}\{keyName}");

            if (key is not null)
            {
                return true;
            }
        }

        return false;
    }
}
