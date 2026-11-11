// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.MachineConsole.Machine;

// Signs out of DDT's session, which ends the console. The agent deletes the session's account once it is signed out.
public static partial class SessionSignOut
{
    private const uint LogOff = 0x00000000;

    // SHTDN_REASON_FLAG_PLANNED, with the major and minor reason Other.
    private const uint Planned = 0x80000000;

    public static void SignOut() => _ = ExitWindowsEx(LogOff, Planned);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ExitWindowsEx(uint flags, uint reason);
}
