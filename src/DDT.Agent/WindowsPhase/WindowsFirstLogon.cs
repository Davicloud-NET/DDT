// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.InstalledPhase;
using DDT.Contracts.Agents;
using Microsoft.Win32;

namespace DDT.Agent.WindowsPhase;

// The commands as a script that an HKLM RunOnce value starts at the next sign-in of an administrator, and the automatic
// logon as Winlogon values with the password as the LSA secret. Only after DDT's session put Winlogon back.
public sealed class WindowsFirstLogon(RegistryKey machine, ISessionAccounts accounts, string scriptFolder, Action<string> createFolder) : IFirstLogon
{
    public const string RunOncePath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";

    public const string ValueName = "DDT first logon";

    public void SetUp(AgentFirstLogon firstLogon)
    {
        ArgumentNullException.ThrowIfNull(firstLogon);

        if (firstLogon.Commands.Count > 0)
        {
            createFolder(scriptFolder);
            string script = Path.Combine(scriptFolder, FirstLogonScript.FileName);
            File.WriteAllBytes(script, FirstLogonScript.File(firstLogon.Commands));

            using RegistryKey runOnce = machine.CreateSubKey(RunOncePath);
            runOnce.SetValue(ValueName, FirstLogonScript.RunOnceCommand(script), RegistryValueKind.String);
        }

        if (firstLogon.AutoLogon is { } autoLogon)
        {
            SignInAutomatically(autoLogon);
        }
    }

    // The secret first, so Winlogon never signs in without it. A password in the key would win over the secret, and
    // every user can read the key. Winlogon counts AutoLogonCount down and turns the automatic logon off at 0.
    private void SignInAutomatically(AgentAutoLogon autoLogon)
    {
        accounts.SetAutoLogonPassword(autoLogon.Password);

        using RegistryKey winlogon = machine.CreateSubKey(DeploySession.WinlogonPath);
        winlogon.DeleteValue("DefaultPassword", throwOnMissingValue: false);
        winlogon.DeleteValue("ForceAutoLogon", throwOnMissingValue: false);
        winlogon.SetValue("DefaultUserName", autoLogon.UserName);
        winlogon.SetValue("DefaultDomainName", autoLogon.Domain ?? Environment.MachineName);
        winlogon.SetValue("AutoLogonCount", autoLogon.Count, RegistryValueKind.DWord);
        winlogon.SetValue("AutoAdminLogon", "1");
    }
}
