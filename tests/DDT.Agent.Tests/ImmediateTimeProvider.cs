// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Tests;

// Every delay completes at once, and its length is recorded so a test can assert the back off. A timer without a
// due time never fires, and changing a timer does nothing, so a stall watchdog armed with CancelAfter stays quiet.
internal sealed class ImmediateTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly Lock _lock = new();
    private readonly List<TimeSpan> _delays = [];

    public List<TimeSpan> Delays
    {
        get
        {
            lock (_lock)
            {
                return [.. _delays];
            }
        }
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            lock (_lock)
            {
                _delays.Add(dueTime);
            }

            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }

        return new CompletedTimer();
    }

    private sealed class CompletedTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
