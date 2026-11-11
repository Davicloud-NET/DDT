// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Concurrent;
using Xunit;

namespace DDT.MachineConsole.Tests;

// The UI thread of a test: what the inbox posts runs here, in order, while the test waits for what it expects.
internal sealed class Pump
{
    private readonly ConcurrentQueue<Action> _posted = new();

    public void Post(Action action) => _posted.Enqueue(action);

    public Task<T> UntilAsync<T>(Func<T?> found)
        where T : class =>
        WaitAsync(found, busy: false);

    // The UI thread is busy with something else. Nothing posted runs, until that many posts wait.
    public Task BusyUntilAsync(int waiting) => WaitAsync(() => _posted.Count >= waiting ? this : null, busy: true);

    private async Task<T> WaitAsync<T>(Func<T?> found, bool busy)
        where T : class
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);

        while (true)
        {
            while (!busy && _posted.TryDequeue(out Action? action))
            {
                action();
            }

            if (found() is { } value)
            {
                return value;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The console did not get there within 10 s.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
