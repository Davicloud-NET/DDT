// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.WindowsPhase;
using DDT.Contracts.Agents;
using Microsoft.Win32;
using Xunit;

namespace DDT.Agent.Tests;

// RunOnce and Winlogon as the registry holds them, under a test key in the current user's hive, which the test deletes.
// The LSA secret is the fake's.
public sealed class WindowsFirstLogonTests : IDisposable
{
    private const string Password = "Kiosk <pass> & \"9\"";

    private readonly string _path = $@"Software\DDT-test-{Guid.NewGuid():N}";
    private readonly RegistryKey _root;
    private readonly FakeSessionAccounts _accounts = new();

    public WindowsFirstLogonTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_path);
    }

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
    }

    private RegistryKey Open(string path) => _root.OpenSubKey(path) ?? throw new InvalidOperationException($"{path} is missing.");

    [Fact]
    public void WritesTheCommandsAsRunOnceValuesInOrder()
    {
        new WindowsFirstLogon(_root, _accounts).SetUp(new AgentFirstLogon(["cmd /c echo one", "msiexec /i C:\\x.msi /qn"], null));

        using RegistryKey runOnce = Open(WindowsFirstLogon.RunOncePath);
        Assert.Equal(["DDT first logon 01", "DDT first logon 02"], runOnce.GetValueNames());
        Assert.Equal("cmd /c echo one", runOnce.GetValue("DDT first logon 01"));
        Assert.Equal("msiexec /i C:\\x.msi /qn", runOnce.GetValue("DDT first logon 02"));
        Assert.Null(_root.OpenSubKey(DeploySession.WinlogonPath));
        Assert.Null(_accounts.StoredAutoLogonPassword);
    }

    [Fact]
    public void SignsInAutomaticallyWithThePasswordOnlyInTheSecret()
    {
        using (RegistryKey before = _root.CreateSubKey(DeploySession.WinlogonPath))
        {
            before.SetValue("DefaultPassword", "left over");
            before.SetValue("ForceAutoLogon", "1");
        }

        new WindowsFirstLogon(_root, _accounts).SetUp(new AgentFirstLogon([], new AgentAutoLogon("kiosk", "CORP", Password, 3)));

        using RegistryKey winlogon = Open(DeploySession.WinlogonPath);
        Assert.Equal("1", winlogon.GetValue("AutoAdminLogon"));
        Assert.Equal("kiosk", winlogon.GetValue("DefaultUserName"));
        Assert.Equal("CORP", winlogon.GetValue("DefaultDomainName"));
        Assert.Equal(3, winlogon.GetValue("AutoLogonCount"));
        Assert.Equal(RegistryValueKind.DWord, winlogon.GetValueKind("AutoLogonCount"));
        Assert.Null(winlogon.GetValue("DefaultPassword"));
        Assert.Null(winlogon.GetValue("ForceAutoLogon"));
        Assert.Equal(Password, _accounts.StoredAutoLogonPassword);
        Assert.Null(_root.OpenSubKey(WindowsFirstLogon.RunOncePath));
    }

    [Fact]
    public void ALocalAccountSignsInToThisComputer()
    {
        new WindowsFirstLogon(_root, _accounts).SetUp(new AgentFirstLogon([], new AgentAutoLogon("Admin", null, Password, 1)));

        using RegistryKey winlogon = Open(DeploySession.WinlogonPath);
        Assert.Equal(Environment.MachineName, winlogon.GetValue("DefaultDomainName"));
    }
}
