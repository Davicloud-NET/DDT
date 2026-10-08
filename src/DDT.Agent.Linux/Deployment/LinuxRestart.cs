// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DDT.Agent.Deployment;

// The restart of the Linux boot environment. The kernel restarts at once and for good, so the restart a run issues
// is carried out when the agent ends, once the server has its last lines, and not at all when the agent goes on.
public sealed class LinuxRestart(AgentLog log, Func<int>? kernelRestart = null, Func<string, bool>? start = null)
{
    public const string Tool = "reboot";

    private readonly Func<int> _kernelRestart = kernelRestart ?? KernelRestart;
    private readonly Func<string, bool> _start = start ?? Start;

    // A run or a change of the boot environment asked for the restart.
    public bool Issued { get; private set; }

    // What lets go of the disks before the kernel restarts, which unmounts nothing itself.
    public Action? Before { get; set; }

    public void Issue() => Issued = true;

    // The agent goes on, so its end later restarts nothing. False when no restart was issued.
    public bool Withdraw()
    {
        bool issued = Issued;
        Issued = false;

        return issued;
    }

    // Returns only when nothing restarted the machine, or the tool that does has just been started. The start script
    // then restarts it, which the agent's exit code asks for.
    public void NowIfIssued()
    {
        if (!Issued)
        {
            return;
        }

        LetGo();

        int error = _kernelRestart();

        if (_start(Tool))
        {
            log.Warning($"The kernel did not restart the machine (error {error}), so {Tool} -f was started for it.");

            return;
        }

        log.Error($"The machine could not restart itself (error {error}, and {Tool} cannot be started). The start script restarts it shortly, or restart it by hand.");
    }

    // Asked for early as well, while the log still reaches the server. What is let go of then is not there later.
    public void LetGo()
    {
        try
        {
            Before?.Invoke();
        }
        catch (Exception exception)
        {
            // Nothing may keep the machine from restarting, so the failure is said and not thrown on
            log.Warning($"The disks could not be let go of before the restart ({exception.Message}).");
        }
    }

    // Everything written reaches the disks first. Returns the error when the kernel is still running after the call.
    private static int KernelRestart()
    {
        RestartNativeMethods.sync();
        RestartNativeMethods.reboot(RestartNativeMethods.Restart);

        return Marshal.GetLastPInvokeError();
    }

    private static bool Start(string tool)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(tool) { UseShellExecute = false, ArgumentList = { "-f" } });

            return process is not null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
