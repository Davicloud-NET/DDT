// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;
using static DDT.Core.Tests.Sequences.TreeSteps;

namespace DDT.Core.Tests.Sequences;

// The Register with Autopilot step, as the validator sees it.
public sealed class AutopilotStepRulesTests
{
    private static RegisterAutopilotStep Register(string? groupTag = null) =>
        new() { Id = Guid.NewGuid(), Name = "Register with Autopilot", GroupTag = groupTag };

    private static SequenceDefinition InWindows(SequenceStep step) => Definition(Partition(), ApplyImage(), step);

    [Fact]
    public void AcceptsTheStepInWindowsAndNeedsVersion8()
    {
        SequenceDefinition definition = InWindows(Register("Sales Vienna-01"));

        Assert.Empty(Validate(definition));
        Assert.Equal(8, definition.RequiredVersion());
        Assert.Equal("sequence.versionTooLow", Assert.Single(Validate(definition with { Version = 7 })).Code);
    }

    [Theory]
    [InlineData("Sales,Vienna")]
    [InlineData("Sales \"A\"")]
    [InlineData("Büro")]
    public void RefusesAGroupTagTheCsvCannotHold(string tag)
    {
        RegisterAutopilotStep register = Register(tag);

        AssertOnly(Validate(InWindows(register)), register, "groupTag", "sequence.autopilotGroupTag");
    }

    [Fact]
    public void RefusesALongGroupTag()
    {
        RegisterAutopilotStep register = Register(new string('A', RegisterAutopilotStep.MaxGroupTagLength + 1));

        AssertOnly(Validate(InWindows(register)), register, "groupTag", "sequence.autopilotGroupTag");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(RegisterAutopilotStep.MaxWaitMinutes + 1)]
    public void WaitsOneToMaxMinutes(int minutes)
    {
        RegisterAutopilotStep register = Register() with { WaitForImport = true, WaitMinutes = minutes };

        AssertOnly(Validate(InWindows(register)), register, "waitMinutes", "sequence.autopilotWaitMinutes");
    }

    [Fact]
    public void RunsInWindowsAndMayRunAgain()
    {
        RegisterAutopilotStep register = Register();

        Assert.Equal((SequencePhase.Windows, 0, true), (register.RequiredPhase, register.RestartLimit, register.Resumable));
    }

    [Fact]
    public void BelongsToInstallingWindows()
    {
        RegisterAutopilotStep register = Register();

        AssertOnly(Validate(WriteRawImage(), register), register, null, "sequence.windowsWithRawImage");
    }
}
