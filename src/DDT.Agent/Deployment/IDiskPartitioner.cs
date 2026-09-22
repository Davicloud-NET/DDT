// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

public interface IDiskPartitioner
{
    // The disks DDT could install on. Every disk probed is logged, with the reason when it is left out.
    Task<IReadOnlyList<LocalDisk>> ListDisksAsync(CancellationToken cancellationToken);

    // Erases the disk and creates the system, Windows and recovery partitions, and reads their partition ids.
    Task<TargetVolumes> PartitionAsync(
        LocalDisk disk,
        int systemPartitionMegabytes,
        int recoveryPartitionMegabytes,
        CancellationToken cancellationToken);

    // Finds a run's partitions again after a restart: Windows PE gave the Windows volume at windowsRoot a letter of
    // its own choosing and the system and recovery partitions none, so they get letters again here.
    Task<TargetVolumes> FindAsync(RunDiskIds ids, string windowsRoot, CancellationToken cancellationToken);
}
