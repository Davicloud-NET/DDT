// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Principal;
using Microsoft.Win32;

namespace DDT.Agent.WindowsPhase;

// Changes the machine's sign-in settings under HKEY_LOCAL_MACHINE for DDT's session, and puts them back. Setup signs in
// its own first user between its restarts, so SignInAutomatically waits for setup to finish. Restore runs when a run
// ends, which can be during setup.
internal sealed class MachineSignInSettings(RegistryKey machine)
{
    // The policies the session changes, as they are now, to be put back at the end.
    public IReadOnlyList<SavedSetting> Policies() =>
        [Saved(DeploySession.SystemPoliciesPath, "HideFastUserSwitching"), Saved(DeploySession.SystemPoliciesPath, "EnableFirstLogonAnimation")];

    // No switching users and no first sign-in animation.
    public void ChangePolicies()
    {
        using RegistryKey policies = machine.CreateSubKey(DeploySession.SystemPoliciesPath);
        policies.SetValue("HideFastUserSwitching", 1, RegistryValueKind.DWord);
        policies.SetValue("EnableFirstLogonAnimation", 0, RegistryValueKind.DWord);
    }

    // Turns on auto-logon that signs in again after a sign-out. The password is the LSA secret Winlogon reads.
    public void SignInAutomatically()
    {
        using RegistryKey winlogon = machine.CreateSubKey(DeploySession.WinlogonPath);
        winlogon.SetValue("AutoAdminLogon", "1");
        winlogon.SetValue("ForceAutoLogon", "1");
        winlogon.SetValue("DefaultUserName", DeploySession.AccountName);
        winlogon.SetValue("DefaultDomainName", Environment.MachineName);

        // A password in the key would win over the LSA secret, and every user can read the key.
        winlogon.DeleteValue("DefaultPassword", throwOnMissingValue: false);
        winlogon.DeleteValue("AutoLogonCount", throwOnMissingValue: false);
    }

    // Turns auto-logon off and removes the values only DDT set. The policies go back to what the file says they were.
    // If the file never said, as after a failure before the service read them, they're removed.
    public void Restore(DeploySessionFile? file)
    {
        using (RegistryKey winlogon = machine.CreateSubKey(DeploySession.WinlogonPath))
        {
            winlogon.SetValue("AutoAdminLogon", "0");
            winlogon.DeleteValue("ForceAutoLogon", throwOnMissingValue: false);
            winlogon.DeleteValue("DefaultPassword", throwOnMissingValue: false);
            winlogon.DeleteValue("AutoLogonCount", throwOnMissingValue: false);

            if (winlogon.GetValue("DefaultUserName") as string == DeploySession.AccountName)
            {
                winlogon.DeleteValue("DefaultUserName");
            }
        }

        IReadOnlyList<SavedSetting> saved = file?.Saved
            ?? [new SavedSetting(DeploySession.SystemPoliciesPath, "HideFastUserSwitching", null, null), new SavedSetting(DeploySession.SystemPoliciesPath, "EnableFirstLogonAnimation", null, null)];

        foreach (SavedSetting setting in saved)
        {
            using RegistryKey key = machine.CreateSubKey(setting.Key);

            if (setting.Text is { } text)
            {
                key.SetValue(setting.Name, text);
            }
            else if (setting.Number is { } number)
            {
                key.SetValue(setting.Name, number, RegistryValueKind.DWord);
            }
            else
            {
                key.DeleteValue(setting.Name, throwOnMissingValue: false);
            }
        }
    }

    // The sign-in screen would offer the deleted account as the last one signed in.
    public void ForgetLastUser(SecurityIdentifier sid)
    {
        using RegistryKey? logonUI = machine.OpenSubKey(DeploySession.LogonUIPath, writable: true);

        if (logonUI is null)
        {
            return;
        }

        if (logonUI.GetValue("LastLoggedOnUserSID") as string == sid.Value)
        {
            foreach (string name in new[] { "LastLoggedOnUser", "LastLoggedOnSAMUser", "LastLoggedOnDisplayName", "LastLoggedOnUserSID" })
            {
                logonUI.DeleteValue(name, throwOnMissingValue: false);
            }
        }

        if (logonUI.GetValue("SelectedUserSID") as string == sid.Value)
        {
            logonUI.DeleteValue("SelectedUserSID");
        }
    }

    private SavedSetting Saved(string key, string name)
    {
        using RegistryKey? opened = machine.OpenSubKey(key);

        return opened?.GetValue(name) switch
        {
            string text => new SavedSetting(key, name, text, null),
            int number => new SavedSetting(key, name, null, number),
            _ => new SavedSetting(key, name, null, null),
        };
    }
}
