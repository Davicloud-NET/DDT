// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// Disks that cannot even be listed, as when the system's list of them cannot be read.
internal sealed class UnlistedDisks(Exception failure) : IDisks
{
    public Task<IReadOnlyList<LocalDisk>> ListDisksAsync(CancellationToken cancellationToken) => Task.FromException<IReadOnlyList<LocalDisk>>(failure);

    public Task<IReadOnlyList<Guid>> CleanAsync(LocalDisk disk, CancellationToken cancellationToken) => Task.FromException<IReadOnlyList<Guid>>(failure);
}
