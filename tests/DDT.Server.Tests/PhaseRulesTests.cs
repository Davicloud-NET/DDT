// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Server.Deployments;
using Xunit;

namespace DDT.Server.Tests;

// What an agent may report and start by the environment it registered from, as a table.
public sealed class PhaseRulesTests
{
    private static readonly SequenceDefinition s_windows = SequenceRequests.Minimal(Guid.NewGuid());
    private static readonly SequenceDefinition s_linux = SequenceRequests.Linux(Guid.NewGuid());

    // Windows PE reports the Windows phase around the hand-over. The service in Windows never goes back.
    [Theory]
    [InlineData(AgentEnvironment.WindowsPE, SequencePhase.WindowsPE, true)]
    [InlineData(AgentEnvironment.WindowsPE, SequencePhase.Windows, true)]
    [InlineData(AgentEnvironment.WindowsPE, SequencePhase.LinuxBoot, false)]
    [InlineData(AgentEnvironment.WindowsPE, SequencePhase.Linux, false)]
    [InlineData(AgentEnvironment.Windows, SequencePhase.WindowsPE, false)]
    [InlineData(AgentEnvironment.Windows, SequencePhase.Windows, true)]
    [InlineData(AgentEnvironment.Windows, SequencePhase.LinuxBoot, false)]
    [InlineData(AgentEnvironment.Windows, SequencePhase.Linux, false)]
    [InlineData(AgentEnvironment.LinuxBoot, SequencePhase.WindowsPE, false)]
    [InlineData(AgentEnvironment.LinuxBoot, SequencePhase.Windows, false)]
    [InlineData(AgentEnvironment.LinuxBoot, SequencePhase.LinuxBoot, true)]
    [InlineData(AgentEnvironment.LinuxBoot, SequencePhase.Linux, true)]
    [InlineData(AgentEnvironment.Linux, SequencePhase.WindowsPE, false)]
    [InlineData(AgentEnvironment.Linux, SequencePhase.Windows, false)]
    [InlineData(AgentEnvironment.Linux, SequencePhase.LinuxBoot, false)]
    [InlineData(AgentEnvironment.Linux, SequencePhase.Linux, true)]
    public void LetsAnEnvironmentReportItsOwnPhasesOnly(AgentEnvironment environment, SequencePhase phase, bool allowed)
    {
        Assert.Equal(allowed, PhaseRules.MayReport(environment, phase));
        Assert.Equal(allowed, PhaseRules.ReportRefusal(environment, phase) is null);
    }

    [Fact]
    public void CoversEveryEnvironmentAndPhaseThereIs()
    {
        // A new environment or phase is refused until the table above says what it may do.
        Assert.Equal(4, Enum.GetValues<AgentEnvironment>().Length);
        Assert.Equal(4, Enum.GetValues<SequencePhase>().Length);
        Assert.False(PhaseRules.MayReport((AgentEnvironment)99, SequencePhase.WindowsPE));
        Assert.False(PhaseRules.MayReport(AgentEnvironment.LinuxBoot, (SequencePhase)99));
    }

    // A boot environment restarts into the other one. An installed system restarts into none.
    [Theory]
    [InlineData(AgentEnvironment.WindowsPE, AgentEnvironment.LinuxBoot)]
    [InlineData(AgentEnvironment.LinuxBoot, AgentEnvironment.WindowsPE)]
    [InlineData(AgentEnvironment.Windows, null)]
    [InlineData(AgentEnvironment.Linux, null)]
    public void NamesTheOtherBootEnvironmentOnlyForABootEnvironment(AgentEnvironment environment, AgentEnvironment? other)
    {
        Assert.Equal(other, PhaseRules.Other(environment));
    }

    [Fact]
    public void SaysWhichEnvironmentRegisteredAndWhichPhaseItCannotReport()
    {
        Assert.Equal(
            "This machine registered from Windows, so its run cannot be in the Windows PE phase. An agent in Windows PE registers before it reports.",
            PhaseRules.ReportRefusal(AgentEnvironment.Windows, SequencePhase.WindowsPE));
        Assert.Equal(
            "This machine registered from Windows PE, so its run cannot be in the Linux boot environment phase. An agent in the Linux boot " +
            "environment registers before it reports.",
            PhaseRules.ReportRefusal(AgentEnvironment.WindowsPE, SequencePhase.LinuxBoot));
        Assert.Equal(
            "This machine registered from the Linux boot environment, so its run cannot be in the Windows phase. An agent in Windows registers " +
            "before it reports.",
            PhaseRules.ReportRefusal(AgentEnvironment.LinuxBoot, SequencePhase.Windows));
        Assert.Equal(
            "This machine registered from the installed Linux, so its run cannot be in the Linux boot environment phase. An agent in the " +
            "Linux boot environment registers before it reports.",
            PhaseRules.ReportRefusal(AgentEnvironment.Linux, SequencePhase.LinuxBoot));
        Assert.Equal(
            "This machine registered from Windows PE, so its run cannot be in the installed Linux phase. An agent in the installed Linux " +
            "registers before it reports.",
            PhaseRules.ReportRefusal(AgentEnvironment.WindowsPE, SequencePhase.Linux));
    }

    // Only the boot environment of a sequence's kind starts it, so an installed system starts none.
    [Theory]
    [InlineData(AgentEnvironment.WindowsPE, true, false)]
    [InlineData(AgentEnvironment.LinuxBoot, false, true)]
    [InlineData(AgentEnvironment.Windows, false, false)]
    [InlineData(AgentEnvironment.Linux, false, false)]
    public void StartsARunOnlyInTheBootEnvironmentOfItsKind(AgentEnvironment environment, bool windows, bool linux)
    {
        Assert.Equal(windows, PhaseRules.Starts(environment, s_windows));
        Assert.Equal(linux, PhaseRules.Starts(environment, s_linux));
        Assert.Equal(linux, PhaseRules.Starts(environment, SequenceRequests.LinuxWithInstalledSteps(Guid.NewGuid())));
    }
}
