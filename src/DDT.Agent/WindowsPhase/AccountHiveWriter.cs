// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Principal;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.ConsoleProtocol;
using Microsoft.Win32;

namespace DDT.Agent.WindowsPhase;

// The session account's own settings, written before its first sign-in: the console as its shell, and none of what
// Ctrl+Alt+Del offers, Task Manager, locking, changing the password and signing out.
internal sealed class AccountHiveWriter(RegistryKey users, IToolRunner tools)
{
    public async Task WriteAsync(SecurityIdentifier sid, string profile, string console, string pipeName, CancellationToken cancellationToken)
    {
        // Signed in, its registry is loaded under its SID; otherwise it is loaded here for the moment.
        string root = sid.Value;
        bool load = false;

        using (RegistryKey? signedIn = users.OpenSubKey(root))
        using (RegistryKey? loaded = users.OpenSubKey(DeploySession.HiveName))
        {
            if (signedIn is null)
            {
                root = DeploySession.HiveName;
                load = loaded is null;
            }
        }

        if (load)
        {
            await tools.RunAsync(OfflineServiceRegistration.RegPath, ["load", $@"HKU\{DeploySession.HiveName}", Path.Combine(profile, "NTUSER.DAT")], cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            Write(root, console, pipeName);
        }
        finally
        {
            if (root == DeploySession.HiveName)
            {
                await tools.RunAsync(OfflineServiceRegistration.RegPath, ["unload", $@"HKU\{DeploySession.HiveName}"], CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private void Write(string root, string console, string pipeName)
    {
        using (RegistryKey winlogon = users.CreateSubKey($@"{root}\{DeploySession.UserWinlogonPath}"))
        {
            winlogon.SetValue("Shell", $"\"{console}\" {ConsolePipe.PipeArgument} {pipeName} {ConsolePipe.SessionArgument}");
        }

        using (RegistryKey policies = users.CreateSubKey($@"{root}\{DeploySession.UserSystemPoliciesPath}"))
        {
            policies.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
            policies.SetValue("DisableLockWorkstation", 1, RegistryValueKind.DWord);
            policies.SetValue("DisableChangePassword", 1, RegistryValueKind.DWord);
        }

        using (RegistryKey explorer = users.CreateSubKey($@"{root}\{DeploySession.UserExplorerPoliciesPath}"))
        {
            explorer.SetValue("NoLogoff", 1, RegistryValueKind.DWord);
        }
    }
}
