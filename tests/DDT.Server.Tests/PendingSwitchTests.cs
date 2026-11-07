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

    // What was picked is only a suggestion for whoever sits at the machine after the restart. It keeps the time it
    // was picked at, so it lapses with the restart's time and not never.
    [Fact]
    public void AnEndedRestartKeepsWhatWasPickedForItUntilTheRestartsTimeIsOver()
    {
        Machine machine = Asked();

        PendingSwitch.End(machine);

        Assert.Null(machine.NextEnvironment);
        Assert.Equal(s_asked, machine.NextEnvironmentUtc);
        Assert.Equal(s_sequence, machine.PendingSequenceId);
        Assert.False(PendingSwitch.Arrives(machine, AgentEnvironment.LinuxBoot, s_asked));
        Assert.False(PendingSwitch.Expire(machine, s_asked + DeploymentLimits.EnvironmentSwitch));
        Assert.Equal(s_sequence, machine.PendingSequenceId);

        Assert.True(PendingSwitch.Expire(machine, s_asked + DeploymentLimits.EnvironmentSwitch + TimeSpan.FromSeconds(1)));

        Assert.Null(machine.PendingSequenceId);
        Assert.Null(machine.NextEnvironmentUtc);
    }

    [Fact]
    public void AnEndedRestartNothingWasPickedForLeavesNothing()
    {
        Machine machine = Asked();
        machine.PendingSequenceId = null;

        PendingSwitch.End(machine);

        Assert.Null(machine.NextEnvironment);
        Assert.Null(machine.NextEnvironmentUtc);
    }

    // The server restarts a machine for its run only so often, and says so once the last restart is over.
    [Fact]
    public void GivesUpOnlyAfterTheLastRestartItOrdersIsOver()
    {
        Guid runId = Guid.NewGuid();
        Machine machine = new() { SmbiosUuid = "4c4c4544-0042-3510-8052-b4c04f4d3232", PrimaryMac = "00155D010203", ActiveDeploymentId = runId };
        Deployment run = new() { Id = runId, Title = "Install Linux", BootEnvironment = AgentEnvironment.LinuxBoot, RestartsOrdered = DeploymentLimits.RestartOrders };

        Assert.Equal(2, DeploymentLimits.RestartOrders);
        Assert.Equal(AgentEnvironment.LinuxBoot, PendingSwitch.GaveUp(machine, run));

        machine.NextEnvironment = AgentEnvironment.LinuxBoot;
        Assert.Null(PendingSwitch.GaveUp(machine, run));

        machine.NextEnvironment = null;
        run.RestartsOrdered = 1;
        Assert.Null(PendingSwitch.GaveUp(machine, run));

        run.RestartsOrdered = 2;
        machine.AgentEnvironment = AgentEnvironment.LinuxBoot;
        Assert.Null(PendingSwitch.GaveUp(machine, run));

        machine.AgentEnvironment = AgentEnvironment.WindowsPE;
        run.State = DDT.Contracts.Deployments.DeploymentState.Running;
        Assert.Null(PendingSwitch.GaveUp(machine, run));

        run.State = DDT.Contracts.Deployments.DeploymentState.Assigned;
        machine.ActiveDeploymentId = Guid.NewGuid();
        Assert.Null(PendingSwitch.GaveUp(machine, run));
        Assert.Null(PendingSwitch.GaveUp(machine, null));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    [InlineData(-1, false)]
    public void AnOrderedRestartIsUnderWayForThirtySeconds(int seconds, bool underWay)
    {
        Machine machine = Asked();

        Assert.Equal(underWay, PendingSwitch.IsOrdered(machine, AgentEnvironment.LinuxBoot, s_asked + TimeSpan.FromSeconds(seconds)));
        Assert.False(PendingSwitch.IsOrdered(machine, AgentEnvironment.WindowsPE, s_asked + TimeSpan.FromSeconds(seconds)));
    }

    // Only a boot environment has another one to restart into.
    [Theory]
    [InlineData(AgentEnvironment.WindowsPE, AgentEnvironment.LinuxBoot)]
    [InlineData(AgentEnvironment.LinuxBoot, AgentEnvironment.WindowsPE)]
    [InlineData(AgentEnvironment.Windows, null)]
    public void EachBootEnvironmentHasTheOtherToRestartInto(AgentEnvironment from, AgentEnvironment? into) =>
        Assert.Equal(into, PhaseRules.Other(from));
}
