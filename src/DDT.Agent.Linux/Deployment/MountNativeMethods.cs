// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.Deployment;

// What mounting a file system needs of libc.
internal static partial class MountNativeMethods
{
    // MS_NOSUID and MS_NODEV of sys/mount.h.
    public const nuint NoSetUserId = 2;
    public const nuint NoDevices = 4;

    // MNT_DETACH of sys/mount.h.
    public const int Detach = 2;

    // data is the file system's own options as text, which the kernel copies.
    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int mount(string source, string target, string fileSystemType, nuint flags, string data);
}
