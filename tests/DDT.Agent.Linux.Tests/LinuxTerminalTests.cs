// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Xunit;

namespace DDT.Agent.Tests;

// The screen of the agent must not stay held after Scroll Lock or Ctrl+S, so it is let go on again and again.
public sealed class LinuxTerminalTests
{
    [Fact]
    public void LetsTheTerminalGoOnOnceASecondUntilItIsToldToStop()
    {
        ManualTimeProvider time = new();
        int released = 0;
        ITimer flowing = new LinuxTerminal(() => released++).KeepFlowing(time);

        Assert.Equal(0, released);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, released);
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(4, released);

        flowing.Dispose();
        time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(4, released);
    }
}
