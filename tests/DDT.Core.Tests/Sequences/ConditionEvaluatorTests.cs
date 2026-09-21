// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Core.Tests.Sequences;

public sealed class ConditionEvaluatorTests
{
    private static readonly MachineVariables s_machine = new(
        "Dell Inc.",
        "Latitude 5440",
        "ABC1234",
        "4c4c4544-0042-3510-8052-b4c04f4d3232",
        ["00155D010203", "00155D0A0B0C"],
        "PC-0042",
        SequencePhase.WindowsPE);

    [Theory]
    [InlineData(ConditionOperator.Equals, "Latitude 5440", true)]
    [InlineData(ConditionOperator.Equals, "Latitude", false)]
    [InlineData(ConditionOperator.NotEquals, "Latitude 5440", false)]
    [InlineData(ConditionOperator.NotEquals, "Latitude 7440", true)]
    [InlineData(ConditionOperator.StartsWith, "Latitude", true)]
    [InlineData(ConditionOperator.StartsWith, "5440", false)]
    [InlineData(ConditionOperator.Contains, "tude 54", true)]
    [InlineData(ConditionOperator.Contains, "Precision", false)]
    public void AppliesEveryOperator(ConditionOperator @operator, string value, bool holds)
    {
        Assert.Equal(holds, ConditionEvaluator.Holds(new StepCondition(MachineVariableNames.Model, @operator, value), s_machine));
    }

    [Theory]
    [InlineData(ConditionOperator.Equals, "LATITUDE 5440", true)]
    [InlineData(ConditionOperator.NotEquals, "latitude 5440", false)]
    [InlineData(ConditionOperator.StartsWith, "latitude", true)]
    [InlineData(ConditionOperator.Contains, "TUDE", true)]
    public void IgnoresCase(ConditionOperator @operator, string value, bool holds)
    {
        Assert.Equal(holds, ConditionEvaluator.Holds(new StepCondition(MachineVariableNames.Model, @operator, value), s_machine));
    }

    [Theory]
    [InlineData(ConditionOperator.Equals, "00:15:5d:0a:0b:0c", true)]
    [InlineData(ConditionOperator.Equals, "00-15-5D-01-02-03", true)]
    [InlineData(ConditionOperator.Equals, "0015.5d01.0203", true)]
    [InlineData(ConditionOperator.Equals, "00:11:22:33:44:55", false)]
    [InlineData(ConditionOperator.NotEquals, "00:15:5D:01:02:03", false)]
    [InlineData(ConditionOperator.NotEquals, "00:11:22:33:44:55", true)]
    [InlineData(ConditionOperator.StartsWith, "00:15:5D", true)]
    [InlineData(ConditionOperator.StartsWith, "00:11:22", false)]
    [InlineData(ConditionOperator.Contains, "0a:0b", true)]
    [InlineData(ConditionOperator.Contains, "01", true)]
    [InlineData(ConditionOperator.Contains, "FF:FF", false)]
    [InlineData(ConditionOperator.Contains, "A0:B0", false)]
    public void MatchesAnyOfTheMacAddressesInAnyNotation(ConditionOperator @operator, string value, bool holds)
    {
        Assert.Equal(holds, ConditionEvaluator.Holds(new StepCondition(MachineVariableNames.MacAddress, @operator, value), s_machine));
    }

    [Fact]
    public void NormalisesTheMacAddressesTheMachineReports()
    {
        StepCondition condition = new(MachineVariableNames.MacAddress, ConditionOperator.Equals, "00155D010203");

        Assert.True(ConditionEvaluator.Holds(condition, s_machine with { MacAddresses = ["00:15:5d:01:02:03"] }));
    }

    [Theory]
    [InlineData(ConditionOperator.Equals, false)]
    [InlineData(ConditionOperator.NotEquals, true)]
    [InlineData(ConditionOperator.StartsWith, false)]
    [InlineData(ConditionOperator.Contains, false)]
    public void TreatsAValueTheMachineDidNotReportAsNoMatch(ConditionOperator @operator, bool holds)
    {
        StepCondition condition = new(MachineVariableNames.Manufacturer, @operator, "Dell");

        Assert.Equal(holds, ConditionEvaluator.Holds(condition, s_machine with { Manufacturer = null }));
    }

    [Theory]
    [InlineData(SequencePhase.WindowsPE, "WindowsPE", true)]
    [InlineData(SequencePhase.WindowsPE, "Windows", false)]
    [InlineData(SequencePhase.Windows, "windows", true)]
    [InlineData(SequencePhase.Windows, "WindowsPE", false)]
    public void TestsThePhase(SequencePhase phase, string value, bool holds)
    {
        StepCondition condition = new(MachineVariableNames.Phase, ConditionOperator.Equals, value);

        Assert.Equal(holds, ConditionEvaluator.Holds(condition, s_machine with { Phase = phase }));
    }

    [Theory]
    [InlineData(MachineVariableNames.Manufacturer, "Dell Inc.")]
    [InlineData(MachineVariableNames.SerialNumber, "abc1234")]
    [InlineData(MachineVariableNames.SmbiosUuid, "4C4C4544-0042-3510-8052-B4C04F4D3232")]
    [InlineData(MachineVariableNames.ComputerName, "pc-0042")]
    public void ReadsEveryVariable(string variable, string value)
    {
        Assert.True(ConditionEvaluator.Holds(new StepCondition(variable, ConditionOperator.Equals, value), s_machine));
    }

    [Theory]
    [InlineData(ConditionOperator.Equals)]
    [InlineData(ConditionOperator.NotEquals)]
    public void NeverLetsAnUnknownVariableHold(ConditionOperator @operator)
    {
        Assert.False(ConditionEvaluator.Holds(new StepCondition("BiosVersion", @operator, "1.0"), s_machine));
    }

    [Fact]
    public void HoldsOnlyWhenEveryConditionHolds()
    {
        StepCondition dell = new(MachineVariableNames.Manufacturer, ConditionOperator.StartsWith, "Dell");
        StepCondition latitude = new(MachineVariableNames.Model, ConditionOperator.StartsWith, "Latitude");
        StepCondition precision = new(MachineVariableNames.Model, ConditionOperator.StartsWith, "Precision");

        Assert.True(ConditionEvaluator.Holds([], s_machine));
        Assert.True(ConditionEvaluator.Holds([dell, latitude], s_machine));
        Assert.False(ConditionEvaluator.Holds([dell, precision], s_machine));
    }
}
