// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Xunit;

namespace DDT.Agent.Tests;

// Time that moves only when a test advances it. Timers that fall due fire on the advancing thread, in order.
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly Lock _lock = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

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
        ManualTimer timer = new(this, callback, state);
        timer.Change(dueTime, period);

        return timer;
    }

    public void Advance(TimeSpan by)
    {
        DateTimeOffset target;

        lock (_lock)
        {
            target = _now + by;
        }

        while (true)
        {
            ManualTimer? due;

            lock (_lock)
            {
                due = _timers.Where(timer => timer.DueAt <= target).MinBy(timer => timer.DueAt);

                if (due is null)
                {
                    _now = target;

                    return;
                }

                _now = due.DueAt!.Value;
                due.DueAt = due.Period == Timeout.InfiniteTimeSpan ? null : _now + due.Period;
            }

            due.Fire();
        }
    }

    // Advances in steps until the condition holds, giving the code under test a moment on the thread pool
    // between steps. Fails the test instead of waiting for ever.
    public async Task AdvanceUntilAsync(TimeSpan step, Func<bool> condition)
    {
        for (int attempt = 0; attempt < 1000; attempt++)
        {
            if (condition())
            {
                return;
            }

            Advance(step);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The condition did not hold after 1000 steps of time.");
    }

    internal void Schedule(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
    {
        lock (_lock)
        {
            if (!_timers.Contains(timer))
            {
                _timers.Add(timer);
            }

            timer.Period = period;
            timer.DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : _now + dueTime;
        }
    }

    internal void Remove(ManualTimer timer)
    {
        lock (_lock)
        {
            _timers.Remove(timer);
        }
    }
}
