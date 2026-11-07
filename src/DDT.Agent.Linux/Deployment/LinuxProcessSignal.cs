// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.Deployment;

// Sends a signal to a process as a list showed it. Its number alone may be another process's by now, so its start is
// read again first: through a descriptor of the process where the kernel gives one, since 5.3, which then holds the
// number, and else right before the kill. startOf is the start in clock ticks, or null for a process that is gone.
internal sealed class LinuxProcessSignal(Func<int, long?> startOf)
{
    // pidfd_open: the descriptor, or the error as a negative number.
    public Func<int, int> Open { get; init; } = OpenDescriptor;

    // pidfd_send_signal: 0 or the error.
    public Func<int, int, int> SendTo { get; init; } = (descriptor, number) =>
        StrayNativeMethods.syscall(StrayNativeMethods.PidfdSendSignal, descriptor, number, 0, 0) == 0 ? 0 : Marshal.GetLastPInvokeError();

    // kill: 0 or the error.
    public Func<int, int, int> Kill { get; init; } = (process, number) =>
        StrayNativeMethods.kill(process, number) == 0 ? 0 : Marshal.GetLastPInvokeError();

    public Action<int> Close { get; init; } = descriptor => _ = StrayNativeMethods.close(descriptor);

    // 0 or the error. A process that is gone, or whose number another process has by now, is no such process.
    public int Send(int process, long started, int number)
    {
        int descriptor = Open(process);

        if (descriptor == -StrayNativeMethods.NoSuchProcess)
        {
            return StrayNativeMethods.NoSuchProcess;
        }

        // No descriptor for any other reason, as on an older kernel or under a filter that refuses the call.
        if (descriptor < 0)
        {
            return startOf(process) == started ? Kill(process, number) : StrayNativeMethods.NoSuchProcess;
        }

        try
        {
            return startOf(process) == started ? SendTo(descriptor, number) : StrayNativeMethods.NoSuchProcess;
        }
        finally
        {
            Close(descriptor);
        }
    }

    private static int OpenDescriptor(int process)
    {
        int descriptor = (int)StrayNativeMethods.syscall(StrayNativeMethods.PidfdOpen, process, 0, 0, 0);

        return descriptor >= 0 ? descriptor : -Marshal.GetLastPInvokeError();
    }
}
