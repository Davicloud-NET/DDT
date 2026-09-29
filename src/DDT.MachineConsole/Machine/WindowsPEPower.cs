// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using Microsoft.Win32;

namespace DDT.MachineConsole.Machine;

// Counts as WinPE only when every sign of it is there: SystemDrive X:, the MiniNT key and wpeutil.exe. A development
// computer has none of them, so a console started there never restarts it.
public sealed class WindowsPEPower : IMachinePower
{
    private const string Wpeutil = @"X:\Windows\System32\wpeutil.exe";

    public WindowsPEPower() => IsWindowsPE = Detect();

    public bool IsWindowsPE { get; }

    public bool CanRestart => IsWindowsPE;

    public void Restart()
    {
        // Checked again right here, not only when the console started.
        if (!IsWindowsPE || !Detect())
        {
            return;
        }

        ProcessStartInfo start = new(Wpeutil) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("reboot");
        using Process? process = Process.Start(start);
    }

    private static bool Detect()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SystemDrive"), "X:", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(Wpeutil))
        {
            return false;
        }

        using RegistryKey? miniNt = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\MiniNT");

        return miniNt is not null;
    }
}
