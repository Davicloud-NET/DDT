// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Machine;
using Xunit;

namespace DDT.MachineConsole.Tests;

// The screen stays on while a run is shown, and may turn off once it is over or the agent has gone.
public sealed class RunDisplayTests
{
    private readonly FakeDisplayPower _power = new();

    [Fact]
    public void HoldsOnceWhileTheRunIsShownAndLetsGoWhenItEnds()
    {
        using RunDisplay screen = new(_power);

        screen.Follow(Scenarios.Running, ended: false);
        screen.Follow(Scenarios.Running, ended: false);
        screen.Follow(Scenarios.Restarting, ended: false);
        bool onDuringTheRun = screen.IsOn;
        screen.Follow(Scenarios.Running with { Stage = ConsoleStage.Finished }, ended: false);

        Assert.True(onDuringTheRun);
        Assert.Equal((1, 0), (_power.Holds, _power.Open));
    }

    [Fact]
    public void LetsGoWhenTheAgentHasGoneOrTheConsoleCloses()
    {
        RunDisplay screen = new(_power);
        screen.Follow(Scenarios.Running, ended: false);
        screen.Follow(Scenarios.Running, ended: true);
        screen.Follow(Scenarios.Running, ended: false);

        screen.Dispose();

        Assert.Equal((2, 0), (_power.Holds, _power.Open));
    }

    [Fact]
    public void NoRunNoHold()
    {
        using RunDisplay screen = new(_power);

        screen.Follow(null, ended: false);
        screen.Follow(Scenarios.Running with { Run = null }, ended: false);

        Assert.Equal(0, _power.Holds);
    }

    // Asked for and given back at once on the test's thread, so this PC's display settings stay as they were.
    [Fact]
    public void TheRealDisplayRequestIsSetAndCleared()
    {
        using (new ThreadDisplayPower().KeepOn())
        {
        }
    }
}
