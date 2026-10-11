// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// Notes which disk the firmware was told to start next, and whether it was also put first.
internal sealed class RecordingDiskStart : IDiskStart
{
    public List<(int Disk, EspPartition? SystemPartition, bool First)> Starts { get; } = [];

    // Runs with every call, for a test that wants to see what was done by then.
    public Action? OnNext { get; set; }

    public void Next(LocalDisk disk, EspPartition? systemPartition, bool first)
    {
        ArgumentNullException.ThrowIfNull(disk);

        Starts.Add((disk.Number, systemPartition, first));
        OnNext?.Invoke();
    }
}
