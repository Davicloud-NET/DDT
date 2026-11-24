// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Win32;

namespace DDT.Agent.BitLocker;

// The FVE policy key in the 64-bit view, where BitLocker reads it.
public sealed class RegistryBitLockerPolicy(RegistryKey machine) : IBitLockerPolicy
{
    public RegistryBitLockerPolicy()
        : this(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
    {
    }

    public IReadOnlyList<string> Absent(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        using RegistryKey? key = machine.OpenSubKey(ActiveDirectoryBackupPolicy.KeyPath);

        return [.. names.Where(name => key?.GetValue(name) is null)];
    }

    public void Set(string name, int value)
    {
        using RegistryKey key = machine.CreateSubKey(ActiveDirectoryBackupPolicy.KeyPath);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    public void Remove(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        bool empty;

        using (RegistryKey? key = machine.OpenSubKey(ActiveDirectoryBackupPolicy.KeyPath, writable: true))
        {
            if (key is null)
            {
                return;
            }

            foreach (string name in names)
            {
                key.DeleteValue(name, throwOnMissingValue: false);
            }

            empty = key.ValueCount == 0 && key.SubKeyCount == 0;
        }

        // Set may have made the key, and an empty policy key says nothing.
        if (empty)
        {
            machine.DeleteSubKey(ActiveDirectoryBackupPolicy.KeyPath, throwOnMissingSubKey: false);
        }
    }
}
