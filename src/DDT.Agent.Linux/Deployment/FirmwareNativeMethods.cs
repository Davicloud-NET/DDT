// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.Deployment;

// What efivarfs needs of libc beyond reading and writing files.
internal static partial class FirmwareNativeMethods
{
    // FS_IOC_GETFLAGS and FS_IOC_SETFLAGS of linux/fs.h. They are declared for a long, and the kernel takes an int.
    public const nuint GetFlags = 0x80086601;
    public const nuint SetFlags = 0x40086602;

    [LibraryImport("libc", SetLastError = true)]
    internal static partial int ioctl(SafeFileHandle file, nuint request, ref long argument);
}
