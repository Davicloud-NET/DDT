// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// Disks that cannot be opened alone while Busy, as Linux refuses a disk a partition of which is mounted.
internal sealed class BusyDisks(IRawDisks disks) : IRawDisks
{
    public const string Message = "/dev/sda is in use: a file system on it is mounted, or a RAID or volume group holds it. Nothing was written to it.";

    public bool Busy { get; set; }

    public int Opened { get; private set; }

    public IRawDisk Open(LocalDisk disk)
    {
        if (Busy)
        {
            throw new DeploymentStepException(Message);
        }

        Opened++;

        return disks.Open(disk);
    }
}
