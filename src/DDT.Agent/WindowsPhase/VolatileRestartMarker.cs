// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security;
using DDT.Agent.Sequences;
using Microsoft.Win32;

namespace DDT.Agent.WindowsPhase;

// Keeps the due restart as a volatile registry key under the service's own key, below root, which is
// HKEY_LOCAL_MACHINE unless a test says otherwise. Windows holds volatile keys in memory only, so every start of Windows
// begins without it, whatever the clock says, and the service's removal takes it along. A marker that cannot be
// written or read only costs the check, so neither fails the run.
public sealed class VolatileRestartMarker(RegistryKey root, AgentLog log) : IRestartMarker
{
    public const string KeyPath = $@"SYSTEM\CurrentControlSet\Services\{OfflineServiceRegistration.ServiceName}\RestartDue";

    public VolatileRestartMarker(AgentLog log)
        : this(Registry.LocalMachine, log)
    {
    }

    public bool IsSet
    {
        get
        {
            try
            {
                using RegistryKey? key = root.OpenSubKey(KeyPath);

                return key is not null;
            }
            catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
            {
                log.Warning($"Whether Windows still has to restart for the run cannot be read ({exception.Message}).");

                return false;
            }
        }
    }

    public void Set()
    {
        try
        {
            root.CreateSubKey(KeyPath, writable: true, RegistryOptions.Volatile).Dispose();
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            log.Warning($"The restart could not be recorded ({exception.Message}). Should the agent stop before Windows restarts, the run goes on without the restart.");
        }
    }
}
