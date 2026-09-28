// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Machines;

// The Windows PE clock can be hours off, so the difference between a batch's sent time and its arrival corrects its
// lines. A difference below the tolerance is the network's delay, and no line is put after the moment it arrived.
public static class MachineLogClock
{
    public static TimeSpan Skew(DateTimeOffset? sentUtc, DateTimeOffset receivedUtc)
    {
        if (sentUtc is not { } sent)
        {
            return TimeSpan.Zero;
        }

        TimeSpan skew = TimeSpan.FromTicks(receivedUtc.UtcTicks - sent.UtcTicks);

        return skew.Duration() < MachineLogLimits.SkewTolerance ? TimeSpan.Zero : skew;
    }

    // In UTC: Npgsql refuses an offset other than zero. The agent's values are whatever it sent, so a time the
    // correction would push out of range becomes the time the line arrived.
    public static DateTimeOffset Corrected(DateTimeOffset agentTime, TimeSpan skew, DateTimeOffset receivedUtc)
    {
        long ticks = agentTime.UtcTicks + skew.Ticks;

        return ticks < 0 || ticks > receivedUtc.UtcTicks
            ? receivedUtc.ToUniversalTime()
            : new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
