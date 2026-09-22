// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Tests;

// Every delay completes at once and moves the clock on by its length, so code that retries for a while runs to its end
// in no time, one delay after another. Only for code that waits for one thing at a time.
internal sealed class AdvancingTimeProvider : TimeProvider
{
    private readonly Lock _lock = new();
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public TimeSpan Elapsed => GetUtcNow() - new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return _now;
        }
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => GetUtcNow().UtcTicks;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            lock (_lock)
            {
                _now += dueTime;
            }

            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }

        return new FiredTimer();
    }

    private sealed class FiredTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
