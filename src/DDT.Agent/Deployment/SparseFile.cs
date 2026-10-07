// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.Deployment;

// Windows keeps a file whole unless it is told to keep it sparse.
public static unsafe class SparseFile
{
    // Not every file system can hold sparse files. Then the file takes its full space.
    public static void Make(SafeFileHandle file) =>
        DiskNativeMethods.DeviceIoControl(file, DiskNativeMethods.FsctlSetSparse, null, 0, null, 0, out _, 0);
}
