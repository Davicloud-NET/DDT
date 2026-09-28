// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Images;

public static class SemaphoreSlimExtensions
{
    // For await using, which releases the semaphore however the block ends.
    public static async Task<SemaphoreHold> EnterAsync(this SemaphoreSlim semaphore, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(semaphore);

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        return new SemaphoreHold(semaphore);
    }
}
