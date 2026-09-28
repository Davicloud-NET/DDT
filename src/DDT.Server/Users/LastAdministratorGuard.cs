// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Images;

namespace DDT.Server.Users;

// Changes that can leave no enabled administrator run one at a time. Two administrators who disable each other at the
// same moment would otherwise both succeed.
internal sealed class LastAdministratorGuard : IDisposable
{
    private readonly SemaphoreSlim _changes = new(1, 1);

    public Task<SemaphoreHold> EnterAsync(CancellationToken cancellationToken) => _changes.EnterAsync(cancellationToken);

    public void Dispose() => _changes.Dispose();
}
