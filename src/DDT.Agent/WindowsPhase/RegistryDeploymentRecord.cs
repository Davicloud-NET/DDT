// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.InstalledPhase;
using Microsoft.Win32;

namespace DDT.Agent.WindowsPhase;

// Every value is a string, so reg query and Get-ItemProperty show them as written.
public sealed class RegistryDeploymentRecord(RegistryKey root, AgentLog log) : IDeploymentRecord
{
    public const string KeyPath = @"SOFTWARE\Davicloud\DDT\Deployment";

    // The 64-bit view, which a 32-bit inventory tool reaches with KEY_WOW64_64KEY.
    public RegistryDeploymentRecord(AgentLog log)
        : this(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64), log)
    {
    }

    public Task WriteAsync(DeploymentRecordEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        root.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);

        using (RegistryKey key = root.CreateSubKey(KeyPath, writable: true))
        {
            foreach ((string name, string? value) in entry.Values())
            {
                if (value is not null)
                {
                    key.SetValue(name, value, RegistryValueKind.String);
                }
            }
        }

        log.Information($"Wrote the deployment record to HKLM\\{KeyPath}: {entry.Result}, run {entry.RunId:D}.");

        return Task.CompletedTask;
    }
}
