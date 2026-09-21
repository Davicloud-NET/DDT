// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Tests;

internal sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
{
    // Both are guarded by the owner's lock.
    public DateTimeOffset? DueAt { get; set; }

    public TimeSpan Period { get; set; } = Timeout.InfiniteTimeSpan;

    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        owner.Schedule(this, dueTime, period);

        return true;
    }

    public void Fire() => callback(state);

    public void Dispose() => owner.Remove(this);

    public ValueTask DisposeAsync()
    {
        Dispose();

        return ValueTask.CompletedTask;
    }
}
