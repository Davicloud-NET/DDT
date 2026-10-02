// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security;
using Microsoft.Win32;

namespace DDT.Core.Windows;

// Whether Microsoft's DHCP Server is installed on this computer and may start. Read from the registry, because the
// service manager hides that service from a service account.
public static class DhcpServerRole
{
    private const string ServiceKey = @"SYSTEM\CurrentControlSet\Services\DHCPServer";
    private const int Disabled = 4;

    // False on another system, and where the key cannot be read.
    public static bool Installed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using RegistryKey? service = Registry.LocalMachine.OpenSubKey(ServiceKey);

            return service?.GetValue("Start") is int start && start != Disabled;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
