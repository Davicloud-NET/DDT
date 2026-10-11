// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.LinuxPhase;
using Xunit;

namespace DDT.Agent.Tests;

// What tells that the administrator's user-data restarts the machine once cloud-init has finished.
public sealed class AnnouncedRestartTests
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    // Delay in minutes plus timeout in seconds plus the margin of two minutes.
    [Theory]
    [InlineData("""{ "mode": "reboot", "delay": "now" }""", "power_state: reboot", 150)]
    [InlineData("""{ "mode": "poweroff", "delay": "+5", "timeout": 120 }""", "power_state: poweroff", 540)]
    [InlineData("""{ "mode": "halt", "delay": "2" }""", "power_state: halt", 270)]
    [InlineData("""{ "mode": "reboot", "delay": 1, "condition": true }""", "power_state: reboot", 210)]
    [InlineData("""{ "mode": "reboot", "condition": "test -f /run/restart" }""", "power_state: reboot", 150)]
    [InlineData("""{ "mode": "reboot", "delay": "soon", "timeout": -4 }""", "power_state: reboot", 150)]
    [InlineData("""{ "delay": 1e300 }""", "power_state: no mode", 150)]
    public void APowerStateAnnouncesARestartWithinItsDelayAndTimeout(string powerState, string what, int seconds)
    {
        AnnouncedRestart? announced = AnnouncedRestart.Read($$"""{ "hostname": "pc", "power_state": {{powerState}} }""", null, s_now);

        Assert.Equal(new AnnouncedRestart(what, TimeSpan.FromSeconds(seconds)), announced);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("""{ "runcmd": ["reboot"] }""")]
    [InlineData("""{ "power_state": "reboot" }""")]
    [InlineData("""{ "power_state": { "mode": "reboot", "condition": false } }""")]
    public void NothingIsAnnouncedWithoutAPowerStateThatCanHappen(string? configuration) =>
        Assert.Null(AnnouncedRestart.Read(configuration, null, s_now));

    [Fact]
    public void AScheduledShutdownAnnouncesItsModeAndItsTime()
    {
        long usec = s_now.AddMinutes(5).ToUnixTimeMilliseconds() * 1000;

        Assert.Equal(
            new AnnouncedRestart("a scheduled poweroff", TimeSpan.FromMinutes(7)),
            AnnouncedRestart.Read(null, $"USEC={usec}\nWARN_WALL=1\nMODE=poweroff\n", s_now));
        Assert.Equal(
            new AnnouncedRestart("a scheduled reboot", AnnouncedRestart.Margin),
            AnnouncedRestart.Read("{}", "MODE=reboot\nUSEC=1\n", s_now));
        Assert.Equal(
            new AnnouncedRestart("a scheduled shutdown", AnnouncedRestart.UnknownDelay + AnnouncedRestart.Margin),
            AnnouncedRestart.Read(null, "", s_now));
    }

    [Fact]
    public void ThePowerStateIsSaidBeforeAShutdownItScheduled() =>
        Assert.Equal(
            "power_state: reboot",
            AnnouncedRestart.Read("""{ "power_state": { "mode": "reboot" } }""", "MODE=reboot\n", s_now)?.What);
}
