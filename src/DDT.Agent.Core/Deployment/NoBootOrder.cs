// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// The boot order as an installed Linux leaves it: the run put the disk first before it was handed over.
public sealed class NoBootOrder : IBootOrder
{
    public Task PutFirstAsync(
        EspPartition esp,
        string loaderPath,
        string description,
        IReadOnlyCollection<Guid> erasedSystemPartitionIds,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RestoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
