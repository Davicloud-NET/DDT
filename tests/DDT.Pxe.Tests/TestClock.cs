// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Pxe.Tests;

// A clock that moves only when a test moves it. A timer that falls due fires on the thread that moves the clock.
internal sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    private readonly Lock _lock = new();
    private readonly List<Timer> _timers = [];
    private DateTimeOffset _now = now;

    // How many timers wait, such as the delay of a loop that has come round again.
    public int Waiting
    {
        get
        {
            lock (_lock)
            {
                return _timers.Count;
            }
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return _now;
        }
    }

    public void Advance(TimeSpan by)
    {
        List<Timer> due;

        lock (_lock)
        {
            _now += by;
            due = [.. _timers.Where(timer => timer.Due <= _now)];
            _timers.RemoveAll(due.Contains);
        }

        foreach (Timer timer in due)
        {
            timer.Fire();
        }
    }

    // Only a timer that fires once, which is all a delay or a timeout needs.
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Timer timer = new(this, callback, state);
        timer.Change(dueTime, period);

        return timer;
    }

    private sealed class Timer(TestClock clock, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset Due { get; private set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._lock)
            {
                clock._timers.Remove(this);

                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    Due = clock._now + dueTime;
                    clock._timers.Add(this);
                }
            }

            return true;
        }

        public void Dispose()
        {
            lock (clock._lock)
            {
                clock._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();

            return ValueTask.CompletedTask;
        }
    }
}
