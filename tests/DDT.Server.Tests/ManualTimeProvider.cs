namespace DDT.Server.Tests;

// A clock that moves only when a test advances it. Timers that fall due fire on the thread that advances it.
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly Lock _lock = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ManualTimer timer = new(this, callback, state);
        timer.Change(dueTime, period);

        return timer;
    }

    // Whether a timer falls due exactly this long from now, such as a timeout that was just armed.
    public bool HasTimerDueIn(TimeSpan delay)
    {
        lock (_lock)
        {
            return _timers.Any(timer => timer.DueUtc == _now + delay);
        }
    }

    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;

        lock (_lock)
        {
            _now += by;
            due = [.. _timers.Where(timer => timer.DueUtc <= _now)];

            foreach (ManualTimer timer in due)
            {
                if (timer.Period > TimeSpan.Zero && timer.Period != Timeout.InfiniteTimeSpan)
                {
                    timer.DueUtc = _now + timer.Period;
                }
                else
                {
                    _timers.Remove(timer);
                }
            }
        }

        foreach (ManualTimer timer in due)
        {
            timer.Fire();
        }
    }

    internal void Schedule(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
    {
        lock (_lock)
        {
            _timers.Remove(timer);

            if (dueTime != Timeout.InfiniteTimeSpan)
            {
                timer.DueUtc = _now + dueTime;
                timer.Period = period;
                _timers.Add(timer);
            }
        }
    }

    internal void Cancel(ManualTimer timer)
    {
        lock (_lock)
        {
            _timers.Remove(timer);
        }
    }
}
