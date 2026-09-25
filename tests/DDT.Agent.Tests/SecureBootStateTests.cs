// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Win32;
using Xunit;

namespace DDT.Agent.Tests;

// The state as Windows records it, under a key of the test's own in the current user's hive, which it deletes.
public sealed class SecureBootStateTests : IDisposable
{
    private readonly string _path = $@"Software\DDT-test-{Guid.NewGuid():N}";
    private readonly RegistryKey _root;

    public SecureBootStateTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_path);
    }

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void ReadsWhatWindowsRecords(int value, bool enabled)
    {
        using RegistryKey state = _root.CreateSubKey(SecureBootState.KeyPath);
        state.SetValue(SecureBootState.ValueName, value, RegistryValueKind.DWord);

        Assert.Equal(enabled, SecureBootState.Read(_root));
    }

    [Fact]
    public void SaysNothingWhereWindowsRecordsNothing()
    {
        Assert.Null(SecureBootState.Read(_root));

        using RegistryKey state = _root.CreateSubKey(SecureBootState.KeyPath);
        state.SetValue(SecureBootState.ValueName, "1");

        Assert.Null(SecureBootState.Read(_root));
    }
}
