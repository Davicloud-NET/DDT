// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.Deployment;

// The immutable flag of a file, which efivarfs sets on every variable the UEFI specification does not name. Until it
// is cleared, the kernel refuses to open such a variable for writing and to delete it.
public static class ImmutableFlag
{
    // FS_IMMUTABLE_FL of linux/fs.h.
    public const long Immutable = 0x10;

    public static void Clear(string path)
    {
        using SafeFileHandle file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long flags = 0;

        // A file system without such flags has no immutable files.
        if (FirmwareNativeMethods.ioctl(file, FirmwareNativeMethods.GetFlags, ref flags) != 0 || (flags & Immutable) == 0)
        {
            return;
        }

        flags &= ~Immutable;

        if (FirmwareNativeMethods.ioctl(file, FirmwareNativeMethods.SetFlags, ref flags) != 0)
        {
            throw new IOException($"The kernel does not let {Path.GetFileName(path)} be changed (error {Marshal.GetLastPInvokeError()}).");
        }
    }
}
