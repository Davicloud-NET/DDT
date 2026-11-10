// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using DDT.MachineConsole.Native;

namespace DDT.MachineConsole.Machine;

// The console restarts a machine only in DDT's Linux boot environment, which it knows by the file the build puts there.
internal sealed class BootEnvironmentPower : IMachinePower
{
    private const string Marker = "/etc/ddt/boot-environment";

    public bool IsWindowsPE => false;

    public bool CanRestart => File.Exists(Marker);

    // Nothing there needs stopping first, as busybox's reboot would do: the agent has ended.
    public void Restart()
    {
        Libc.Sync();
        using Process? reboot = Process.Start(new ProcessStartInfo("reboot", "-f") { UseShellExecute = false });
    }
}
