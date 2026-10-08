// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.Deployment;

// The disk devices of the running Linux.
public sealed class KernelBlockDevices : IBlockDevices
{
    // .NET opens no file with O_EXCL unless it creates it, and on a block device that flag is what asks to be alone.
    public SafeFileHandle OpenAlone(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        int descriptor = DiskNativeMethods.open(path, DiskNativeMethods.ReadWrite | DiskNativeMethods.Exclusive | DiskNativeMethods.DataSync | DiskNativeMethods.CloseOnExec);

        if (descriptor < 0)
        {
            int error = Marshal.GetLastPInvokeError();

            throw new IOException($"{path} cannot be opened ({Marshal.GetPInvokeErrorMessage(error)}).", error);
        }

        return new SafeFileHandle(descriptor, ownsHandle: true);
    }

    public void ReadPartitionsAgain(SafeFileHandle device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (DiskNativeMethods.ioctl(device, DiskNativeMethods.RereadPartitionTable, 0) != 0)
        {
            int error = Marshal.GetLastPInvokeError();

            throw new IOException(Marshal.GetPInvokeErrorMessage(error), error);
        }
    }
}
