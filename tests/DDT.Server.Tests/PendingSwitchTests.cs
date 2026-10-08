// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using Xunit;

namespace DDT.Server.Tests;

// How long a restart into the other boot environment counts, to the second.
public sealed class PendingSwitchTests
{
    private static readonly DateTimeOffset s_asked = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid s_sequence = Guid.Parse("0197a3c0-0000-7000-8000-00000000000d");

    private static Machine Asked() => new()
    {
        SmbiosUuid = "4c4c4544-0042-3510-8052-b4c04f4d3232",
        PrimaryMac = "00155D010203",
        NextEnvironment = AgentEnvironment.LinuxBoot,
        NextEnvironmentUtc = s_asked,
        PendingSequenceId = s_sequence,
    };

    [Fact]
    public void ARestartMayTakeFifteenMinutes() => Assert.Equal(TimeSpan.FromMinutes(15), DeploymentLimits.EnvironmentSwitch);

    [Theory]
    [InlineData(0, true)]
    [InlineData(15 * 60, true)]
    [InlineData((15 * 60) + 1, false)]
    public void CountsUntilTheLimitAndNotASecondLonger(int seconds, bool open)
    {
        Machine machine = Asked();
        DateTimeOffset now = s_asked + TimeSpan.FromSeconds(seconds);

        Assert.Equal(open, PendingSwitch.IsOpen(machine, now));
        Assert.Equal(open, PendingSwitch.Arrives(machine, AgentEnvironment.LinuxBoot, now));
        Assert.False(PendingSwitch.Arrives(machine, AgentEnvironment.WindowsPE, now));
        Assert.Equal(!open, PendingSwitch.Expire(machine, now));
        Assert.Equal(open ? AgentEnvironment.LinuxBoot : null, machine.NextEnvironment);
        Assert.Equal(open ? s_asked : null, machine.NextEnvironmentUtc);
        Assert.Equal(open ? s_sequence : null, machine.PendingSequenceId);
    }

    // A time ahead of the clock is left from before the clock was set back, or comes from a host with another clock.
    [Theory]
    [InlineData(1)]
    [InlineData(15 * 60)]
    [InlineData(24 * 60 * 60)]
    public void ATimeInTheFutureOpensNoWindow(int secondsAhead)
    {
        Machine machine = Asked();
        DateTimeOffset now = s_asked - TimeSpan.FromSeconds(secondsAhead);

        Assert.False(PendingSwitch.IsOpen(machine, now));
        Assert.False(PendingSwitch.IsOpen(s_asked, now));
        Assert.False(PendingSwitch.Arrives(machine, AgentEnvironment.LinuxBoot, now));
    }

    [Fact]
    public void AMachineThatWasNotAskedHasNothingToExpire()
    {
        Machine machine = new() { SmbiosUuid = "4c4c4544-0042-3510-8052-b4c04f4d3232", PrimaryMac = "00155D010203", PendingSequenceId = s_sequence };

        Assert.False(PendingSwitch.IsOpen(machine, s_asked));
        Assert.False(PendingSwitch.Expire(machine, s_asked.AddDays(1)));
        Assert.Equal(s_sequence, machine.PendingSequenceId);
    }

    [Fact]
    public void AnEndedRestartKeepsWhatWasPickedForIt()
    {
        Machine machine = Asked();

        PendingSwitch.End(machine);

        Assert.Null(machine.NextEnvironment);
        Assert.Null(machine.NextEnvironmentUtc);
        Assert.Equal(s_sequence, machine.PendingSequenceId);
    }

    // Only a boot environment has another one to restart into.
    [Theory]
    [InlineData(AgentEnvironment.WindowsPE, AgentEnvironment.LinuxBoot)]
    [InlineData(AgentEnvironment.LinuxBoot, AgentEnvironment.WindowsPE)]
    [InlineData(AgentEnvironment.Windows, null)]
    public void EachBootEnvironmentHasTheOtherToRestartInto(AgentEnvironment from, AgentEnvironment? into) =>
        Assert.Equal(into, PhaseRules.Other(from));
}
