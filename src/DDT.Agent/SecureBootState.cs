// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security;
using Microsoft.Win32;

namespace DDT.Agent;

// Whether the firmware started Windows with Secure Boot on. Windows records this at every start, WinPE included.
public static class SecureBootState
{
    public const string KeyPath = @"SYSTEM\CurrentControlSet\Control\SecureBoot\State";
    public const string ValueName = "UEFISecureBootEnabled";

    // Null when Windows does not say, as on firmware without UEFI.
    public static bool? Read() => Read(Registry.LocalMachine);

    public static bool? Read(RegistryKey root)
    {
        ArgumentNullException.ThrowIfNull(root);

        try
        {
            using RegistryKey? state = root.OpenSubKey(KeyPath);

            return state?.GetValue(ValueName) is int enabled ? enabled != 0 : null;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
