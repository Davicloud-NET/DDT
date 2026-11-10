// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.Consoles;

// What stopping the console and taking its terminal back need of libc.
internal static partial class ConsoleNativeMethods
{
    public const int StandardInput = 0;
    public const int Terminate = 15;

    // KDSETMODE and KD_TEXT of linux/kd.h.
    public const nuint SetMode = 0x4B3A;
    public const nint TextMode = 0;

    [LibraryImport("libc", SetLastError = true)]
    internal static partial int kill(int process, int signal);

    [LibraryImport("libc", SetLastError = true)]
    internal static partial int ioctl(int file, nuint request, nint argument);
}
