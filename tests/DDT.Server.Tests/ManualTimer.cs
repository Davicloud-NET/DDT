// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Tests;

// Its schedule is kept and changed only under the clock's lock.
internal sealed class ManualTimer(ManualTimeProvider clock, TimerCallback callback, object? state) : ITimer
{
    public DateTimeOffset DueUtc { get; set; }

    public TimeSpan Period { get; set; }

    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        clock.Schedule(this, dueTime, period);

        return true;
    }

    public void Fire() => callback(state);

    public void Dispose() => clock.Cancel(this);

    public ValueTask DisposeAsync()
    {
        Dispose();

        return ValueTask.CompletedTask;
    }
}
