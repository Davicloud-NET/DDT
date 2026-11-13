// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.InstalledPhase;
using DDT.Contracts.Agents;
using Microsoft.Win32;

namespace DDT.Agent.WindowsPhase;

// The commands as HKLM RunOnce values, which Windows runs at the next sign-in of an administrator, and the automatic
// logon as Winlogon values with the password as the LSA secret. Only after DDT's session put Winlogon back.
public sealed class WindowsFirstLogon(RegistryKey machine, ISessionAccounts accounts) : IFirstLogon
{
    public const string RunOncePath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";

    // Numbered, so they're written and listed in the step's order.
    public static string ValueName(int index) => string.Create(CultureInfo.InvariantCulture, $"DDT first logon {index + 1:00}");

    public void SetUp(AgentFirstLogon firstLogon)
    {
        ArgumentNullException.ThrowIfNull(firstLogon);

        if (firstLogon.Commands.Count > 0)
        {
            using RegistryKey runOnce = machine.CreateSubKey(RunOncePath);

            for (int index = 0; index < firstLogon.Commands.Count; index++)
            {
                runOnce.SetValue(ValueName(index), firstLogon.Commands[index], RegistryValueKind.String);
            }
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
