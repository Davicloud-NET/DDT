// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// The one disk of a dry run's machine. A file next to root stands in for it once a raw disk image is written.
public sealed class DryRunDisks(string root, AgentLog log) : IDisks
{
    public static LocalDisk Disk { get; } = new(0, "Dry run disk", 128L * 1024 * 1024 * 1024, StorageBusType.Virtual, 0);

    public Task<IReadOnlyList<LocalDisk>> ListDisksAsync(CancellationToken cancellationToken)
    {
        log.Information($"Dry run: {Disk.Describe()} stands in for this computer's disks.");

        return Task.FromResult<IReadOnlyList<LocalDisk>>([Disk]);
    }

    // Deletes the file that stands in for the disk, the way cleaning removes what a real disk held.
    public Task<IReadOnlyList<Guid>> CleanAsync(LocalDisk disk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(disk);

        File.Delete(FileRawDisks.PathFor(root, disk.Number));
        log.Information($"Dry run: no disk is cleaned. On a machine, the partition table of disk {disk.Number} and what it held would be erased now.");

        return Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}
