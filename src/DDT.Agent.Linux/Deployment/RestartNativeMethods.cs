// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.Deployment;

// What restarting the machine needs of libc.
internal static partial class RestartNativeMethods
{
    // RB_AUTOBOOT of sys/reboot.h, the kernel's LINUX_REBOOT_CMD_RESTART.
    public const int Restart = 0x01234567;

    [LibraryImport("libc")]
    internal static partial void sync();

    [LibraryImport("libc", SetLastError = true)]
    internal static partial int reboot(int type);

    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int umount2(string target, int flags);
}
