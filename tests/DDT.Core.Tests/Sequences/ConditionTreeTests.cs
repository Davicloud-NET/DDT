// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Core.Tests.Sequences;

public sealed class ConditionTreeTests
{
    private static readonly MachineVariables s_machine = new(
        "  Dell   Inc. ",
        "Latitude  5440",
        "ABC1234",
        "4c4c4544-0042-3510-8052-b4c04f4d3232",
        ["00155D010203", "00:15:5d:0a:0b:0c"],
        "PC-0042",
        SequencePhase.WindowsPE)
    {
        DeviceKind = DeviceKind.Laptop,
        SecureBootEnabled = true,
        Facts = new MachineFacts
        {
            MemoryMegabytes = 16384,
            ProcessorName = "13th Gen Intel(R) Core(TM) i7-1365U",
            ProcessorCores = 10,
            LogicalProcessors = 12,
            TpmPresent = true,
            TpmVersion = "2.0",
            SecureBootCapable = true,
            IPv4Address = "10.1.2.34",
            IPv4PrefixLength = 24,
            DefaultGateway = "10.1.2.1",
            DnsSuffix = "corp.example",
            DhcpServer = "10.1.2.5",
            SystemVersion = "Not Specified",
            SystemFamily = "Latitude",
            SystemSku = "0C0A",
            AssetTag = "INV-0042",
            BaseboardProduct = "0K2P1X",
            BiosVersion = "1.18.0",
            BiosDate = "2025-03-14",
        },
        Variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Office"] = "ProPlus",
            [MachineVariableNames.LastStepFailed] = "false",
            [MachineVariableNames.LastExitCode] = "3010",
            ["ddt.disk.system"] = "0193a4b2-0000-7000-8000-000000000001",
        },
    };

    [Theory]
    [InlineData(ConditionOperator.Equals, "Latitude 5440", true)]
    [InlineData(ConditionOperator.Equals, "Latitude", false)]
    [InlineData(ConditionOperator.NotEquals, "latitude 5440", false)]
    [InlineData(ConditionOperator.NotEquals, "Latitude 7440", true)]
    [InlineData(ConditionOperator.StartsWith, "latitude", true)]
    [InlineData(ConditionOperator.StartsWith, "5440", false)]
    [InlineData(ConditionOperator.EndsWith, " 5440", true)]
    [InlineData(ConditionOperator.EndsWith, "Latitude", false)]
    [InlineData(ConditionOperator.Contains, "TUDE 54", true)]
    [InlineData(ConditionOperator.Contains, "Precision", false)]
    [InlineData(ConditionOperator.NotContains, "Precision", true)]
    [InlineData(ConditionOperator.NotContains, "tude", false)]
    [InlineData(ConditionOperator.Matches, "latitude 5?40", true)]
    [InlineData(ConditionOperator.Matches, "Lat*", true)]
    [InlineData(ConditionOperator.Matches, "*54*", true)]
    [InlineData(ConditionOperator.Matches, "*5?4", false)]
    [InlineData(ConditionOperator.Matches, "Latitude", false)]
    [InlineData(ConditionOperator.In, "Latitude 7440; latitude 5440 ;", true)]
    [InlineData(ConditionOperator.In, "Latitude 7440;Precision", false)]
    [InlineData(ConditionOperator.Exists, "", true)]
    [InlineData(ConditionOperator.NotExists, "", false)]
    public void AppliesEveryOperatorToText(ConditionOperator @operator, string value, bool holds)
    {
        Assert.Equal(holds, Holds(MachineVariableNames.Model, @operator, value));
    }

    // Firmware writes model names with stray spaces, so the tree compares them cleaned, like model rules and driver
    // packages do.
    [Theory]
    [InlineData(MachineVariableNames.Model, "  LATITUDE   5440 ")]
    [InlineData(MachineVariableNames.Manufacturer, "dell inc.")]
    [InlineData(MachineVariableNames.FriendlyModel, "latitude 5440")]
    public void ComparesModelNamesCleaned(string variable, string value)
    {
        Assert.True(Holds(variable, ConditionOperator.Equals, value));
    }

    [Theory]
    [InlineData(ConditionOperator.Equals, false)]
    [InlineData(ConditionOperator.Matches, false)]
    [InlineData(ConditionOperator.Exists, false)]
    [InlineData(ConditionOperator.NotEquals, true)]
    [InlineData(ConditionOperator.NotExists, true)]
    public void TakesABoardMakersPlaceholderForNoModel(ConditionOperator @operator, bool holds)
    {
        MachineVariables unset = s_machine with { Manufacturer = "System manufacturer", Model = "To Be Filled By O.E.M." };

        Assert.Equal(holds, ConditionEvaluator.Holds(new TestCondition(MachineVariableNames.Model, @operator, "To Be*"), unset));
        Assert.Equal(holds, ConditionEvaluator.Holds(new TestCondition(MachineVariableNames.Manufacturer, @operator, "System*"), unset));
    }

    [Theory]
    [InlineData(MachineVariableNames.MemoryMegabytes, ConditionOperator.Greater, "8192", true)]
    [InlineData(MachineVariableNames.MemoryMegabytes, ConditionOperator.Greater, "16384", false)]
    [InlineData(MachineVariableNames.MemoryMegabytes, ConditionOperator.GreaterOrEqual, "16384", true)]
    [InlineData(MachineVariableNames.MemoryMegabytes, ConditionOperator.Less, "16384", false)]
    [InlineData(MachineVariableNames.MemoryMegabytes, ConditionOperator.Less, "32768", true)]
    [InlineData(MachineVariableNames.MemoryMegabytes, ConditionOperator.LessOrEqual, "16384.0", true)]
    [InlineData(MachineVariableNames.MemoryMegabytes, ConditionOperator.Equals, "16384.00", true)]
    [InlineData(MachineVariableNames.MemoryMegabytes, ConditionOperator.NotEquals, "16384", false)]
    [InlineData(MachineVariableNames.MemoryMegabytes, ConditionOperator.In, "8192;16384", true)]
    [InlineData(MachineVariableNames.MemoryMegabytes, ConditionOperator.Greater, "lots", false)]
    [InlineData(MachineVariableNames.MemoryMegabytes, ConditionOperator.LessOrEqual, "lots", false)]
    [InlineData(MachineVariableNames.ProcessorCores, ConditionOperator.GreaterOrEqual, "8", true)]
    [InlineData(MachineVariableNames.LogicalProcessors, ConditionOperator.Equals, "12", true)]
    [InlineData(MachineVariableNames.TpmVersion, ConditionOperator.Equals, "2", true)]
    [InlineData(MachineVariableNames.TpmVersion, ConditionOperator.GreaterOrEqual, "1.2", true)]
    [InlineData(MachineVariableNames.IPv4PrefixLength, ConditionOperator.Less, "16", false)]
    [InlineData(MachineVariableNames.LastExitCode, ConditionOperator.Equals, "3010", true)]
    [InlineData(MachineVariableNames.LastExitCode, ConditionOperator.In, "0;3010", true)]
    public void ComparesNumbersAsNumbers(string variable, ConditionOperator @operator, string value, bool holds)
    {
        Assert.Equal(holds, Holds(variable, @operator, value));
    }

    [Theory]
    [InlineData(MachineVariableNames.TpmPresent, ConditionOperator.Equals, "true", true)]
    [InlineData(MachineVariableNames.TpmPresent, ConditionOperator.Equals, "Yes", true)]
    [InlineData(MachineVariableNames.TpmPresent, ConditionOperator.Equals, "1", true)]
    [InlineData(MachineVariableNames.TpmPresent, ConditionOperator.Equals, "no", false)]
    [InlineData(MachineVariableNames.TpmPresent, ConditionOperator.NotEquals, "false", true)]
    [InlineData(MachineVariableNames.SecureBootCapable, ConditionOperator.Equals, "TRUE", true)]
    [InlineData(MachineVariableNames.SecureBootEnabled, ConditionOperator.Equals, "yes", true)]
    [InlineData(MachineVariableNames.LastStepFailed, ConditionOperator.Equals, "no", true)]
    [InlineData(MachineVariableNames.LastStepFailed, ConditionOperator.Equals, "true", false)]
    public void ComparesYesOrNoByMeaning(string variable, ConditionOperator @operator, string value, bool holds)
    {
        Assert.Equal(holds, Holds(variable, @operator, value));
    }

    [Theory]
    [InlineData(MachineVariableNames.IPv4Address, ConditionOperator.InSubnet, "10.1.2.0/24", true)]
    [InlineData(MachineVariableNames.IPv4Address, ConditionOperator.InSubnet, "10.1.0.0/16", true)]
    [InlineData(MachineVariableNames.IPv4Address, ConditionOperator.InSubnet, "10.1.2.99/24", true)]
    [InlineData(MachineVariableNames.IPv4Address, ConditionOperator.InSubnet, "10.1.2.32/28", true)]
    [InlineData(MachineVariableNames.IPv4Address, ConditionOperator.InSubnet, "10.1.2.0/27", false)]
    [InlineData(MachineVariableNames.IPv4Address, ConditionOperator.InSubnet, "10.1.3.0/24", false)]
    [InlineData(MachineVariableNames.IPv4Address, ConditionOperator.InSubnet, "0.0.0.0/0", true)]
    [InlineData(MachineVariableNames.IPv4Address, ConditionOperator.InSubnet, "10.1.2.34/32", true)]
    [InlineData(MachineVariableNames.IPv4Address, ConditionOperator.InSubnet, "10.1.2.0/33", false)]
    [InlineData(MachineVariableNames.IPv4Address, ConditionOperator.InSubnet, "10.1.2.0", false)]
    [InlineData(MachineVariableNames.IPv4Address, ConditionOperator.InSubnet, "10.1/16", false)]
    [InlineData(MachineVariableNames.IPv4Address, ConditionOperator.Equals, "10.1.2.034", true)]
    [InlineData(MachineVariableNames.IPv4Address, ConditionOperator.Equals, "10.1.2.3", false)]
    [InlineData(MachineVariableNames.IPv4Address, ConditionOperator.StartsWith, "10.1.", true)]
    [InlineData(MachineVariableNames.IPv4Address, ConditionOperator.In, "10.1.2.33;10.1.2.34", true)]
    [InlineData(MachineVariableNames.DefaultGateway, ConditionOperator.InSubnet, "10.1.2.0/24", true)]
    [InlineData(MachineVariableNames.DhcpServer, ConditionOperator.Equals, "10.1.2.5", true)]
    [InlineData(MachineVariableNames.Subnet, ConditionOperator.Equals, "10.1.2.0/24", true)]
    [InlineData(MachineVariableNames.DnsSuffix, ConditionOperator.EndsWith, ".example", true)]
    public void TestsTheNetwork(string variable, ConditionOperator @operator, string value, bool holds)
    {
        Assert.Equal(holds, Holds(variable, @operator, value));
    }

    [Theory]
    [InlineData(ConditionOperator.Equals, "00:15:5d:0a:0b:0c", true)]
    [InlineData(ConditionOperator.Equals, "0015.5d01.0203", true)]
    [InlineData(ConditionOperator.NotEquals, "00-15-5D-01-02-03", false)]
    [InlineData(ConditionOperator.NotEquals, "00:11:22:33:44:55", true)]
    [InlineData(ConditionOperator.StartsWith, "00-15-5D", true)]
    [InlineData(ConditionOperator.Contains, "0a:0b", true)]
    [InlineData(ConditionOperator.Contains, "A0:B0", false)]
    [InlineData(ConditionOperator.NotContains, "A0:B0", true)]
    [InlineData(ConditionOperator.NotContains, "0A", false)]
    [InlineData(ConditionOperator.EndsWith, "0b:0c", true)]
    [InlineData(ConditionOperator.EndsWith, "B0C", false)]
    [InlineData(ConditionOperator.Matches, "00:15:5D:0?:*", true)]
    [InlineData(ConditionOperator.Matches, "00:11:*", false)]
    [InlineData(ConditionOperator.In, "00:11:22:33:44:55; 00-15-5D-01-02-03", true)]
    [InlineData(ConditionOperator.Exists, "", true)]
    public void MatchesAnyMacAddressAtByteBoundaries(ConditionOperator @operator, string value, bool holds)
    {
        Assert.Equal(holds, Holds(MachineVariableNames.MacAddress, @operator, value));
    }

    [Fact]
    public void ReadsThePrimaryMacAddressAndTheOtherFacts()
    {
        Assert.True(Holds(MachineVariableNames.PrimaryMacAddress, ConditionOperator.Equals, "00:15:5D:01:02:03"));
        Assert.False(Holds(MachineVariableNames.PrimaryMacAddress, ConditionOperator.Equals, "00:15:5D:0A:0B:0C"));
        Assert.True(ConditionEvaluator.Holds(
            new TestCondition(MachineVariableNames.PrimaryMacAddress, ConditionOperator.Equals, "00155D0A0B0C"),
            s_machine with { PrimaryMacAddress = "00155D0A0B0C" }));
        Assert.True(Holds(MachineVariableNames.DeviceKind, ConditionOperator.Equals, "laptop"));
        Assert.True(Holds(MachineVariableNames.ProcessorName, ConditionOperator.Contains, "i7-1365U"));
        Assert.True(Holds(MachineVariableNames.SystemFamily, ConditionOperator.Equals, "Latitude"));
        Assert.True(Holds(MachineVariableNames.SystemSku, ConditionOperator.Equals, "0C0A"));
        Assert.True(Holds(MachineVariableNames.AssetTag, ConditionOperator.StartsWith, "INV-"));
        Assert.True(Holds(MachineVariableNames.BaseboardProduct, ConditionOperator.Equals, "0K2P1X"));
        Assert.True(Holds(MachineVariableNames.BiosVersion, ConditionOperator.Matches, "1.18.*"));
        Assert.True(Holds(MachineVariableNames.BiosDate, ConditionOperator.StartsWith, "2025-"));
        Assert.True(Holds(MachineVariableNames.SystemVersion, ConditionOperator.Equals, "Not Specified"));
        Assert.True(Holds(MachineVariableNames.SerialNumber, ConditionOperator.Equals, "abc1234"));
        Assert.True(Holds(MachineVariableNames.SmbiosUuid, ConditionOperator.StartsWith, "4C4C4544"));
        Assert.True(Holds(MachineVariableNames.ComputerName, ConditionOperator.Equals, "pc-0042"));
        Assert.True(Holds(MachineVariableNames.Phase, ConditionOperator.Equals, "WindowsPE"));
    }

    // Lenovo's model is a type number such as 21HD. The name people know is its system version.
    [Fact]
    public void KnowsALenovoByItsSystemVersion()
    {
        MachineVariables lenovo = s_machine with
        {
            Manufacturer = "LENOVO",
            Model = "21HDCTO1WW",
            Facts = s_machine.Facts! with { SystemVersion = "ThinkPad T14 Gen 4" },
        };

        Assert.Equal("ThinkPad T14 Gen 4", lenovo.FriendlyModel);
        Assert.True(ConditionEvaluator.Holds(new TestCondition(MachineVariableNames.FriendlyModel, ConditionOperator.Matches, "ThinkPad T14*"), lenovo));
        Assert.Equal("21HDCTO1WW", (lenovo with { Facts = lenovo.Facts! with { SystemVersion = "Not Specified" } }).FriendlyModel);
        Assert.Equal("Latitude  5440", s_machine.FriendlyModel);
    }

    [Fact]
    public void TestsTheRunsValuesAndVariablesIgnoringCase()
    {
        Assert.True(Holds("Office", ConditionOperator.Equals, "proplus"));
        Assert.True(Holds("OFFICE", ConditionOperator.In, "Standard;ProPlus"));
        Assert.True(Holds("model", ConditionOperator.StartsWith, "Latitude"));
        Assert.True(Holds("lastexitcode", ConditionOperator.Greater, "0"));
    }

    // A name without a value only meets the negative operators.
    [Theory]
    [InlineData(ConditionOperator.Equals, false)]
    [InlineData(ConditionOperator.NotEquals, true)]
    [InlineData(ConditionOperator.StartsWith, false)]
    [InlineData(ConditionOperator.EndsWith, false)]
    [InlineData(ConditionOperator.Contains, false)]
    [InlineData(ConditionOperator.NotContains, true)]
    [InlineData(ConditionOperator.Matches, false)]
    [InlineData(ConditionOperator.In, false)]
    [InlineData(ConditionOperator.Exists, false)]
    [InlineData(ConditionOperator.NotExists, true)]
    [InlineData(ConditionOperator.Greater, false)]
    [InlineData(ConditionOperator.GreaterOrEqual, false)]
    [InlineData(ConditionOperator.Less, false)]
    [InlineData(ConditionOperator.LessOrEqual, false)]
    [InlineData(ConditionOperator.InSubnet, false)]
    public void TakesANameWithoutAValueAsNoMatch(ConditionOperator @operator, bool holds)
    {
        MachineVariables bare = s_machine with { Facts = null, SerialNumber = null };

        Assert.Equal(holds, Holds("Region", @operator, "*"));
        Assert.Equal(holds, ConditionEvaluator.Holds(new TestCondition(MachineVariableNames.IPv4Address, @operator, "0.0.0.0/0"), bare));
        Assert.Equal(holds, ConditionEvaluator.Holds(new TestCondition(MachineVariableNames.SerialNumber, @operator, "1"), bare));
    }

    [Fact]
    public void NeverLetsAnUnknownOperatorOrAnEmptyTestHold()
    {
        Assert.False(Holds(MachineVariableNames.Model, (ConditionOperator)99, "Latitude 5440"));
        Assert.False(ConditionEvaluator.Holds(new TestCondition("", ConditionOperator.NotExists), s_machine));
        Assert.False(ConditionEvaluator.Holds(new TestCondition(null!, ConditionOperator.NotEquals, "x"), s_machine));
    }

    // A value a step or the run's start set wins over the name the machine was assigned, for legacy conditions too.
    [Fact]
    public void TakesTheLatestComputerName()
    {
        MachineVariables renamed = s_machine with { Variables = new Dictionary<string, string> { ["computername"] = "PC-NEW" } };

        Assert.True(ConditionEvaluator.Holds(new TestCondition(MachineVariableNames.ComputerName, ConditionOperator.Equals, "PC-NEW"), renamed));
        Assert.True(ConditionEvaluator.Holds(new StepCondition(MachineVariableNames.ComputerName, ConditionOperator.Equals, "PC-NEW"), renamed));
        Assert.Equal("PC-0042", s_machine.Value(MachineVariableNames.ComputerName));
    }

    // Facts come from the machine. A run variable with the same name doesn't change them.
    [Fact]
    public void KeepsTheFactsWhateverTheVariablesSay()
    {
        MachineVariables shadowed = s_machine with { Variables = new Dictionary<string, string> { [MachineVariableNames.Model] = "Precision" } };

        Assert.True(ConditionEvaluator.Holds(new TestCondition(MachineVariableNames.Model, ConditionOperator.StartsWith, "Latitude"), shadowed));
    }

    [Fact]
    public void HoldsForEmptyAllAndNoneButNotForEmptyAny()
    {
        Assert.True(ConditionEvaluator.Holds(new AllCondition(), s_machine));
        Assert.True(ConditionEvaluator.Holds(new NoneCondition(), s_machine));
        Assert.False(ConditionEvaluator.Holds(new AnyCondition(), s_machine));
        Assert.True(ConditionEvaluator.Holds((ConditionNode?)null, s_machine));
        Assert.True(ConditionEvaluator.Holds(new AllCondition { Parts = null! }, s_machine));
    }

    [Fact]
    public void CombinesNestedGroups()
    {
        TestCondition dell = new(MachineVariableNames.Manufacturer, ConditionOperator.StartsWith, "Dell");
        TestCondition lenovo = new(MachineVariableNames.Manufacturer, ConditionOperator.Equals, "LENOVO");
        TestCondition memory = new(MachineVariableNames.MemoryMegabytes, ConditionOperator.GreaterOrEqual, "8192");
        TestCondition virtualMachine = new(MachineVariableNames.DeviceKind, ConditionOperator.Equals, "Virtual");

        Assert.True(ConditionEvaluator.Holds(Any(dell, lenovo), s_machine));
        Assert.False(ConditionEvaluator.Holds(All(dell, lenovo), s_machine));
        Assert.True(ConditionEvaluator.Holds(All(Any(dell, lenovo), memory, None(virtualMachine)), s_machine));
        Assert.False(ConditionEvaluator.Holds(All(Any(dell, lenovo), None(memory)), s_machine));
        Assert.True(ConditionEvaluator.Holds(None(All(lenovo, memory), virtualMachine), s_machine));
        Assert.True(ConditionEvaluator.Holds(Any(All(), lenovo), s_machine));
        Assert.False(ConditionEvaluator.Holds(All(Any(None(dell)), memory), s_machine));
    }

    // The validator refuses a null part. If one slips through anyway, it doesn't hold.
    [Fact]
    public void TakesAPartThatIsNullAsOneThatDoesNotHold()
    {
        TestCondition dell = new(MachineVariableNames.Manufacturer, ConditionOperator.StartsWith, "Dell");

        Assert.False(ConditionEvaluator.Holds(new AllCondition { Parts = [dell, null!] }, s_machine));
        Assert.True(ConditionEvaluator.Holds(new AnyCondition { Parts = [null!, dell] }, s_machine));
    }

    [Fact]
    public void RecordsEveryTestWithItsPathAndTheValueItWasTestedAgainst()
    {
        ConditionNode test = All(
            new TestCondition(MachineVariableNames.MacAddress, ConditionOperator.Contains, "0A:0B"),
            Any(
                new TestCondition(MachineVariableNames.Model, ConditionOperator.Contains, "Precision"),
                new TestCondition("Region", ConditionOperator.Equals, "EU")),
            None(new TestCondition(MachineVariableNames.MacAddress, ConditionOperator.NotEquals, "00:11:22:33:44:55")));

        ConditionResult result = ConditionEvaluator.Evaluate(test, s_machine, ConditionEvaluator.TestPath);

        Assert.False(result.Held);
        Assert.Equal(
            [
                new TestEvaluation("test.parts[0]", true, "00:15:5d:0a:0b:0c"),
                new TestEvaluation("test.parts[1].parts[0]", false, "Latitude  5440"),
                new TestEvaluation("test.parts[1].parts[1]", false, null),
                new TestEvaluation("test.parts[2].parts[0]", true, "00155D010203, 00:15:5d:0a:0b:0c"),
            ],
            result.Evaluations);
    }

    [Fact]
    public void RecordsARootTestAtItsPath()
    {
        ConditionResult result = ConditionEvaluator.Evaluate(
            new TestCondition(MachineVariableNames.LastExitCode, ConditionOperator.Equals, "0"),
            s_machine,
            ConditionEvaluator.UntilPath);

        Assert.False(result.Held);
        Assert.Equal([new TestEvaluation("until", false, "3010")], result.Evaluations);

        ConditionResult none = ConditionEvaluator.Evaluate(null, s_machine, ConditionEvaluator.WhenPath);

        Assert.True(none.Held);
        Assert.Empty(none.Evaluations);
    }

    [Fact]
    public void EvaluatesAStepsConditionsAndItsWhenTogether()
    {
        RebootStep step = new()
        {
            Id = Guid.NewGuid(),
            Name = "Restart",
            Conditions =
            [
                new StepCondition(MachineVariableNames.Manufacturer, ConditionOperator.StartsWith, "  Dell"),
                new StepCondition(MachineVariableNames.Phase, ConditionOperator.Equals, "Windows"),
            ],
            When = All(new TestCondition(MachineVariableNames.MemoryMegabytes, ConditionOperator.Greater, "8192")),
        };

        ConditionResult result = ConditionEvaluator.Evaluate(step, s_machine);

        Assert.False(result.Held);
        Assert.Equal(
            [
                new TestEvaluation("conditions[0]", true, "  Dell   Inc. "),
                new TestEvaluation("conditions[1]", false, "WindowsPE"),
                new TestEvaluation("when.parts[0]", true, "16384"),
            ],
            result.Evaluations);
        MachineVariables windows = s_machine with { Phase = SequencePhase.Windows };

        Assert.True(ConditionEvaluator.Evaluate(step, windows).Held);
        Assert.False(ConditionEvaluator.Evaluate(step with { When = Any() }, windows).Held);
        Assert.True(ConditionEvaluator.Evaluate(new RebootStep { Id = Guid.NewGuid(), Name = "Plain" }, s_machine) is { Held: true, Evaluations: [] });
    }

    // The legacy conditions keep their old rules. A version 1 or 2 agent runs them without cleaning.
    [Fact]
    public void KeepsTheLegacyRulesForConditions()
    {
        Assert.False(ConditionEvaluator.Holds(new StepCondition(MachineVariableNames.Model, ConditionOperator.Equals, "Latitude 5440"), s_machine));
        Assert.True(ConditionEvaluator.Holds(new StepCondition(MachineVariableNames.Model, ConditionOperator.Equals, "latitude  5440"), s_machine));
        Assert.False(ConditionEvaluator.Holds(new StepCondition("Office", ConditionOperator.NotEquals, "Standard"), s_machine));
        Assert.False(ConditionEvaluator.Holds(new StepCondition(MachineVariableNames.Model, ConditionOperator.Matches, "Lat*"), s_machine));
    }

    [Fact]
    public void BoundsWhatItRecords()
    {
        string longModel = new('x', 300);
        ConditionNode many = All([.. Enumerable.Range(0, 40).Select(_ => new TestCondition(MachineVariableNames.Model, ConditionOperator.Exists))]);

        ConditionResult result = ConditionEvaluator.Evaluate(many, s_machine with { Model = longModel }, ConditionEvaluator.WhenPath);

        Assert.True(result.Held);
        Assert.Equal(TestEvaluation.MaxPerNode, result.Evaluations.Count);
        Assert.All(result.Evaluations, evaluation => Assert.Equal(TestEvaluation.MaxActualLength, evaluation.Actual!.Length));
        Assert.Equal("when.parts[31]", result.Evaluations[^1].Path);
    }

    [Fact]
    public void TypesEveryNameByTheCatalogue()
    {
        Assert.Equal(FactType.Number, ConditionEvaluator.TypeOf("memorymegabytes"));
        Assert.Equal(FactType.Mac, ConditionEvaluator.TypeOf(MachineVariableNames.PrimaryMacAddress));
        Assert.Equal(FactType.IPv4, ConditionEvaluator.TypeOf(MachineVariableNames.DhcpServer));
        Assert.Equal(FactType.YesNo, ConditionEvaluator.TypeOf(MachineVariableNames.LastStepFailed));
        Assert.Equal(FactType.Text, ConditionEvaluator.TypeOf("Office"));
    }

    private static bool Holds(string variable, ConditionOperator @operator, string value) =>
        ConditionEvaluator.Holds(new TestCondition(variable, @operator, value), s_machine);

    private static AllCondition All(params ConditionNode[] parts) => new() { Parts = parts };

    private static AnyCondition Any(params ConditionNode[] parts) => new() { Parts = parts };

    private static NoneCondition None(params ConditionNode[] parts) => new() { Parts = parts };
}
