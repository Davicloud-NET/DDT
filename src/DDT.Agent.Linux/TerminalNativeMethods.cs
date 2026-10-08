// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent;

// What letting a held terminal go on needs of libc.
internal static partial class TerminalNativeMethods
{
    public const int StandardOutput = 1;

    // TCXONC of asm-generic/ioctls.h, with TCOOFF and TCOON of asm-generic/termbits.h.
    public const nuint FlowControl = 0x540A;
    public const nint OutputOff = 0;
    public const nint OutputOn = 1;

    [LibraryImport("libc", SetLastError = true)]
    internal static partial int ioctl(int file, nuint request, nint argument);
}
