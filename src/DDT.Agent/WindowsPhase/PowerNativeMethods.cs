// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.WindowsPhase;

// Windows' power requests, which keep the system out of sleep while one is set.
internal static partial class PowerNativeMethods
{
    public const int PowerRequestSystemRequired = 1;

    public static readonly nint InvalidHandle = -1;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint PowerCreateRequest(in PowerReasonContext context);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PowerSetRequest(nint request, int type);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PowerClearRequest(nint request, int type);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);
}
