// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// One MemoryRawDisk per disk number, kept across openings as a real disk keeps what was written to it.
internal sealed class MemoryRawDisks : IRawDisks
{
    public Dictionary<int, MemoryRawDisk> Disks { get; } = [];

    public int SectorSize { get; set; } = 512;

    public IRawDisk Open(LocalDisk disk)
    {
        ArgumentNullException.ThrowIfNull(disk);

        if (!Disks.TryGetValue(disk.Number, out MemoryRawDisk? raw))
        {
            raw = new MemoryRawDisk(disk.SizeBytes, SectorSize);
            Disks[disk.Number] = raw;
        }

        return raw;
    }
}
