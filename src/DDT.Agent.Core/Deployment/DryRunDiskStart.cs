// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// Says what a boot environment would tell the firmware to send the machine back to its disk.
public sealed class DryRunDiskStart(AgentLog log) : IDiskStart
{
    public void Next(LocalDisk disk, EspPartition? systemPartition, bool first)
    {
        ArgumentNullException.ThrowIfNull(disk);

        log.Information(first
            ? $"Dry run: the firmware is not changed. On a machine, the boot entry of disk {disk.Number} would be put first again and named in BootNext, so the next start comes from the disk."
            : $"Dry run: the firmware is not changed. On a machine, BootNext would name the boot entry of disk {disk.Number}, so the next start comes from the disk.");
    }
}
