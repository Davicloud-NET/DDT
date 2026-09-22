// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DDT.Agent.WindowsPhase;

// Lists the path for the session manager, which deletes what MoveFileEx listed, in order, early in the next start of
// Windows and before any service starts. Only SYSTEM or an administrator may list anything.
public sealed class MoveFileRestartDeleter(AgentLog log) : IRestartDeleter
{
    // MOVEFILE_DELAY_UNTIL_REBOOT.
    public const uint DelayUntilReboot = 0x4;

    public void DeleteAtRestart(string path)
    {
        if (!FileNativeMethods.MoveFileEx(path, null, DelayUntilReboot))
        {
            log.Warning($"{path} stays after the next start of Windows: it could not be marked for deletion ({new Win32Exception(Marshal.GetLastPInvokeError()).Message}).");
        }
    }
}
