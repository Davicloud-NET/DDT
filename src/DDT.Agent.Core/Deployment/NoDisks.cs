// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// The disks of an installed system for a run that only goes on there: none to choose, and none to erase.
public sealed class NoDisks : IDisks
{
    public const string Message = "A run that goes on in the installed system erases no disk.";

    public Task<IReadOnlyList<LocalDisk>> ListDisksAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LocalDisk>>([]);

    public Task<IReadOnlyList<Guid>> CleanAsync(LocalDisk disk, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<Guid>>(new DeploymentStepException(Message));
}
