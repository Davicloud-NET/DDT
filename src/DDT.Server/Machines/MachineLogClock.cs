// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Machines;

// The Windows PE clock can be hours off. A batch says when the agent sent it by its own clock, and the difference to
// the server's clock when it arrives corrects every line in it. A difference below the tolerance is the network's
// delay rather than the clock's, and a line is never put after the moment it arrived.
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
