// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using Microsoft.Win32;

namespace DDT.MachineConsole.Machine;

// What the console may do to the machine once the agent has ended: restart it, which only ever happens in Windows PE.
public interface IMachinePower
{
    bool IsWindowsPE { get; }

    // False outside Windows PE, where the console never restarts anything.
    bool CanRestart { get; }

    void Restart();
}

// The machine the console runs on. It counts as Windows PE only when every sign of it is there: the system drive X:,
// the MiniNT key Windows PE sets, and wpeutil.exe. A development computer has none of them, so a console started there
// never restarts it.
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
