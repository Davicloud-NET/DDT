// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.Deployment;

// The kernel's mount calls. What the agent mounts is a share or a seed, so none of it may hold a device or a program
// that runs as another user.
public sealed class LibcMounts : IMounts
{
    // For a call that failed without an error number.
    private const int Unknown = -1;

    public int Mount(string source, string target, string fileSystem, string options) =>
        MountNativeMethods.mount(source, target, fileSystem, MountNativeMethods.NoSetUserId | MountNativeMethods.NoDevices, options) == 0
            ? 0
            : Error();

    public int Unmount(string target, bool detach) =>
        RestartNativeMethods.umount2(target, detach ? MountNativeMethods.Detach : 0) == 0 ? 0 : Error();

    private static int Error() => Marshal.GetLastPInvokeError() is not 0 and int error ? error : Unknown;
}
