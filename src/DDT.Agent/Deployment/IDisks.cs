// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

public interface IDisks
{
    // The disks DDT could install on. Every disk probed is logged, with the reason when it is left out.
    Task<IReadOnlyList<LocalDisk>> ListDisksAsync(CancellationToken cancellationToken);

    // Erases the disk's partition table, because a raw disk image brings its own. Returns the unique GUIDs of the EFI
    // system partitions it erased, which old firmware boot entries may still name.
    Task<IReadOnlyList<Guid>> CleanAsync(LocalDisk disk, CancellationToken cancellationToken);
}
