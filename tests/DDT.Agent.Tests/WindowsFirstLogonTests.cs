// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.WindowsPhase;
using DDT.Contracts.Agents;
using Microsoft.Win32;
using Xunit;

namespace DDT.Agent.Tests;

// RunOnce and Winlogon as the registry holds them, under a test key in the current user's hive, and the script in a
// temporary folder, which the test deletes. The LSA secret is the fake's.
public sealed class WindowsFirstLogonTests : IDisposable
{
    private const string Password = "Kiosk <pass> & \"9\"";

    private readonly string _path = $@"Software\DDT-test-{Guid.NewGuid():N}";
    private readonly string _parent = Directory.CreateTempSubdirectory("ddt-first-logon-").FullName;
    private readonly RegistryKey _root;
    private readonly FakeSessionAccounts _accounts = new();

    public WindowsFirstLogonTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_path);
    }

    private string Folder => Path.Combine(_parent, "FirstLogon");

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
        Directory.Delete(_parent, recursive: true);
    }

    private WindowsFirstLogon FirstLogon() => new(_root, _accounts, Folder, folder => Directory.CreateDirectory(folder));

    private RegistryKey Open(string path) => _root.OpenSubKey(path) ?? throw new InvalidOperationException($"{path} is missing.");

    [Fact]
    public void WritesOneScriptAndOneRunOnceValueThatStartsIt()
    {
        FirstLogon().SetUp(new AgentFirstLogon(["cmd /c echo one", "msiexec /i C:\\x.msi /qn"], null));

        using RegistryKey runOnce = Open(WindowsFirstLogon.RunOncePath);
        string script = Path.Combine(Folder, FirstLogonScript.FileName);
        Assert.Equal([WindowsFirstLogon.ValueName], runOnce.GetValueNames());
        Assert.Equal($"\"{Path.Combine(Environment.SystemDirectory, "cmd.exe")}\" /d /c \"{script}\"", runOnce.GetValue(WindowsFirstLogon.ValueName));
        Assert.Equal(RegistryValueKind.String, runOnce.GetValueKind(WindowsFirstLogon.ValueName));
        Assert.True(File.Exists(script));
        Assert.Null(_root.OpenSubKey(DeploySession.WinlogonPath));
        Assert.Null(_accounts.StoredAutoLogonPassword);
    }

    [Fact]
    public void WithoutCommandsThereIsNoScriptAndNoRunOnceValue()
    {
        FirstLogon().SetUp(new AgentFirstLogon([], new AgentAutoLogon("Admin", null, Password, 1)));

        Assert.False(Directory.Exists(Folder));
        Assert.Null(_root.OpenSubKey(WindowsFirstLogon.RunOncePath));
    }

    [Fact]
    public void SignsInAutomaticallyWithThePasswordOnlyInTheSecret()
    {
        using (RegistryKey before = _root.CreateSubKey(DeploySession.WinlogonPath))
        {
            before.SetValue("DefaultPassword", "left over");
            before.SetValue("ForceAutoLogon", "1");
        }

        FirstLogon().SetUp(new AgentFirstLogon([], new AgentAutoLogon("kiosk", "CORP", Password, 3)));

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
        FirstLogon().SetUp(new AgentFirstLogon([], new AgentAutoLogon("Admin", null, Password, 1)));

        using RegistryKey winlogon = Open(DeploySession.WinlogonPath);
        Assert.Equal(Environment.MachineName, winlogon.GetValue("DefaultDomainName"));
    }
}
