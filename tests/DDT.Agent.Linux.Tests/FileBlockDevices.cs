// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.Tests;

// Files in the place of disk devices. BusyTimes makes that many openings fail as a device does that something holds.
internal sealed class FileBlockDevices : IBlockDevices
{
    public int BusyTimes { get; set; }

    public List<string> Opened { get; } = [];

    public int Rereads { get; private set; }

    // What reading the partition table again fails with, as the kernel does while a partition is in use.
    public IOException? RereadFailure { get; set; }

    public SafeFileHandle OpenAlone(string path)
    {
        Opened.Add(path);

        if (BusyTimes > 0)
        {
            BusyTimes--;

            throw new IOException("Device or resource busy", IBlockDevices.Busy);
        }

        return File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    public void ReadPartitionsAgain(SafeFileHandle device)
    {
        Rereads++;

        if (RereadFailure is { } failure)
        {
            throw failure;
        }
    }
}
