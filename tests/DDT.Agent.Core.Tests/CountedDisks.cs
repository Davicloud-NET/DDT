// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// Lists the same disks as another lister without going through it, and counts how often it was asked.
internal sealed class CountedDisks(IReadOnlyList<LocalDisk> disks) : IDisks
{
    private int _listed;

    public int Listed => Volatile.Read(ref _listed);

    public Task<IReadOnlyList<LocalDisk>> ListDisksAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _listed);

        return Task.FromResult(disks);
    }

    public Task<IReadOnlyList<Guid>> CleanAsync(LocalDisk disk, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The look never cleans a disk.");
}
