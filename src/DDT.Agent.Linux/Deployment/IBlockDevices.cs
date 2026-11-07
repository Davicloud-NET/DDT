// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.Deployment;

// What only the kernel can do with a disk's device. The tests put a file in the device's place.
public interface IBlockDevices
{
    // EBUSY, the HResult of the IOException for a device that something else holds.
    const int Busy = 16;

    // Opens the device for reading and writing, alone and written through. The kernel refuses with Busy while a file
    // system on the disk is mounted or a RAID or volume group holds it. Throws IOException with the error as HResult.
    SafeFileHandle OpenAlone(string path);

    // Makes the kernel read the disk's partition table again. Throws IOException when it does not.
    void ReadPartitionsAgain(SafeFileHandle device);
}
