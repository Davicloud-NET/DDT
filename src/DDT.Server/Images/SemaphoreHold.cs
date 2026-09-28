// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Images;

public sealed class SemaphoreHold(SemaphoreSlim semaphore) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        semaphore.Release();

        return ValueTask.CompletedTask;
    }
}
