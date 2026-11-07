// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.Deployment;

// What a disk's device needs of libc. The flags are those of x86-64 Linux (asm-generic/fcntl.h).
internal static partial class DiskNativeMethods
{
    public const int ReadWrite = 0x2;
    public const int Exclusive = 0x80;
    public const int DataSync = 0x1000;
    public const int CloseOnExec = 0x80000;

    // BLKRRPART of linux/fs.h.
    public const nuint RereadPartitionTable = 0x125F;

    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int open(string path, int flags);

    [LibraryImport("libc", SetLastError = true)]
    internal static partial int ioctl(SafeFileHandle file, nuint request, nint argument);
}
