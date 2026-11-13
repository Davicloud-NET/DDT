// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Win32;

namespace DDT.Agent.Applications;

// Windows keeps a key per package provisioned for all users, named by its full name, in the all-user store.
public sealed class RegistryProvisionedPackages : IProvisionedPackages
{
    public const string ApplicationsPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Applications";

    public IReadOnlyList<string> FullNames()
    {
        using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? applications = machine.OpenSubKey(ApplicationsPath);

        return applications?.GetSubKeyNames() ?? [];
    }
}
