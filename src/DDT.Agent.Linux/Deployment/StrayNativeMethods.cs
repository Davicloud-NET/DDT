// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.Deployment;

// What ending a script's left-over processes needs of libc.
internal static partial class StrayNativeMethods
{
    // PR_SET_CHILD_SUBREAPER of linux/prctl.h.
    public const int SetChildSubreaper = 36;

    // SIGKILL, which no process can catch.
    public const int KillSignal = 9;

    // SIGTERM, which asks a process to end and lets it clean up.
    public const int TerminateSignal = 15;

    // pidfd_open and pidfd_send_signal of asm-generic/unistd.h, which every architecture numbers alike.
    public const int PidfdOpen = 434;
    public const int PidfdSendSignal = 424;

    // ESRCH of errno.h.
    public const int NoSuchProcess = 3;

    // Declared with every argument the kernel reads: prctl takes a variable number in C.
    [LibraryImport("libc", SetLastError = true)]
    internal static partial int prctl(int option, nuint argument2, nuint argument3, nuint argument4, nuint argument5);

    [LibraryImport("libc", SetLastError = true)]
    internal static partial int kill(int process, int signal);

    // For the calls an older libc has no function for. Declared with four arguments, the most these two read.
    [LibraryImport("libc", SetLastError = true)]
    internal static partial nint syscall(nint number, nint first, nint second, nint third, nint fourth);

    [LibraryImport("libc", SetLastError = true)]
    internal static partial int close(int descriptor);
}
