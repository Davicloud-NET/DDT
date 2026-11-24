// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.BitLocker;
using Microsoft.Win32;
using Xunit;

namespace DDT.Agent.Tests;

// The policy key under a test key in the current user's hive, which the test deletes.
public sealed class RegistryBitLockerPolicyTests : IDisposable
{
    private readonly string _path = $@"Software\DDT-test-{Guid.NewGuid():N}";
    private readonly RegistryKey _root;

    public RegistryBitLockerPolicyTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_path);
    }

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
    }

    [Fact]
    public void LeavesNoKeyBehindWhereNoneWas()
    {
        RegistryBitLockerPolicy policy = new(_root);

        policy.Set("OSRecovery", 1);
        policy.Set("OSActiveDirectoryBackup", 1);
        policy.Remove(["OSRecovery", "OSActiveDirectoryBackup"]);

        using RegistryKey? key = _root.OpenSubKey(ActiveDirectoryBackupPolicy.KeyPath);
        Assert.Null(key);
        Assert.Equal(["OSRecovery"], policy.Absent(["OSRecovery"]));
    }

    [Fact]
    public void KeepsTheKeyAndTheValuesThatWereThere()
    {
        using (RegistryKey before = _root.CreateSubKey(ActiveDirectoryBackupPolicy.KeyPath))
        {
            before.SetValue("OSRecovery", 1, RegistryValueKind.DWord);
        }

        RegistryBitLockerPolicy policy = new(_root);
        Assert.Equal(["OSActiveDirectoryBackup"], policy.Absent(["OSRecovery", "OSActiveDirectoryBackup"]));

        policy.Set("OSActiveDirectoryBackup", 1);
        policy.Remove(["OSActiveDirectoryBackup"]);

        using RegistryKey? key = _root.OpenSubKey(ActiveDirectoryBackupPolicy.KeyPath);
        Assert.Equal(["OSRecovery"], Assert.IsType<RegistryKey>(key).GetValueNames());
    }
}
