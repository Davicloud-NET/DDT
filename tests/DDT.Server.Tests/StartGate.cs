// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Extensions.Hosting;

namespace DDT.Server.Tests;

// Holds the host's start until the test's thread listens for it, as a host lifetime, which a start waits for first.
internal sealed class StartGate : IHostLifetime
{
    private readonly TaskCompletionSource _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Open() => _listening.TrySetResult();

    public Task WaitForStartAsync(CancellationToken cancellationToken) => _listening.Task.WaitAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
