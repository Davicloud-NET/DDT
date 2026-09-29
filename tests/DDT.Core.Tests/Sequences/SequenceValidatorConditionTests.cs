// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;
using static DDT.Core.Tests.Sequences.TreeSteps;

namespace DDT.Core.Tests.Sequences;

// Condition trees, meaning a node's When, an IF's Test and a repeat's Until. A problem's field points inside the tree.
public sealed class SequenceValidatorConditionTests
{
    private static TestCondition Test(string variable, ConditionOperator op, string value = "") => new(variable, op, value);

    private static AllCondition All(params ConditionNode[] parts) => new() { Parts = parts };

    private static AnyCondition Any(params ConditionNode[] parts) => new() { Parts = parts };

    private static NoneCondition None(params ConditionNode[] parts) => new() { Parts = parts };

    private static IReadOnlyList<SequenceProblem> ValidateWhen(ConditionNode when, out RebootStep step)
    {
        step = Reboot() with { When = when };

        return Validate(Definition(Partition(), step) with
        {
            Variables = [new VariableDeclaration { Name = "Office" }],
            Inputs = [new InputDeclaration { Name = "Installer", Label = "Installer", Kind = InputKind.Account }],
        });
    }

    private static void AssertOnlyAt(ConditionNode when, string field, string code)
    {
        IReadOnlyList<SequenceProblem> problems = ValidateWhen(when, out RebootStep step);

        AssertOnly(problems, step, field, code);
    }

    [Fact]
    public void AcceptsATreeOverFactsAndValues()
    {
        ConditionNode when = All(
            Any(Test(MachineVariableNames.FriendlyModel, ConditionOperator.Matches, "ThinkPad*"), Test("model", ConditionOperator.StartsWith, "20")),
            None(Test(MachineVariableNames.DeviceKind, ConditionOperator.Equals, "Virtual")),
            Test(MachineVariableNames.MemoryMegabytes, ConditionOperator.GreaterOrEqual, "8192"),
            Test(MachineVariableNames.ProcessorCores, ConditionOperator.In, "4; 8;16"),
            Test(MachineVariableNames.TpmPresent, ConditionOperator.Equals, "yes"),
            Test(MachineVariableNames.IPv4Address, ConditionOperator.InSubnet, "10.0.0.0/8"),
            Test(MachineVariableNames.DefaultGateway, ConditionOperator.In, "10.0.0.1;10.0.0.254"),
            Test(MachineVariableNames.PrimaryMacAddress, ConditionOperator.StartsWith, "00:15:5D"),
            Test(MachineVariableNames.MacAddress, ConditionOperator.In, "00:15:5D:01:02:03;00-15-5D-01-02-04"),
            Test(MachineVariableNames.AssetTag, ConditionOperator.NotExists),
            Test("Office", ConditionOperator.Equals, "ProPlus"),

            // A value from rules or machine roles. The validator can't know it or its type.
            Test("Site", ConditionOperator.GreaterOrEqual, "3"));

        Assert.Empty(ValidateWhen(when, out _));
    }

    // Only rules and machine roles can set these values, and only the server knows them.
    [Fact]
    public void ListsTheNamesOnlyRulesAndMachineRolesCanGiveAValue()
    {
        RebootStep step = Reboot() with
        {
            When = Any(
                Test("Site", ConditionOperator.Equals, "Vienna"),
                Test("site", ConditionOperator.Exists),
                Test("Office", ConditionOperator.Exists),
                Test(MachineVariableNames.Model, ConditionOperator.Equals, "Latitude 7440"),
                Test(MachineVariableNames.LastStepFailed, ConditionOperator.Equals, "true"),
                Test("TimeZone", ConditionOperator.Exists)),
        };
        IfStep branch = If([Reboot()]) with { Test = Test("Building", ConditionOperator.Exists) };

        SequenceAnalysis analysis = SequenceValidator.Analyse(Definition(Partition(), step, branch) with
        {
            Variables = [new VariableDeclaration { Name = "Office" }],
        });

        Assert.Empty(analysis.Problems);
        Assert.Equal(["Site", "Building"], analysis.ValueNames);
    }

    [Fact]
    public void NamesTheFieldInsideTheTree()
    {
        AssertOnlyAt(
            All(Test(MachineVariableNames.Model, ConditionOperator.Equals, "Latitude 7440"), Any(Test("Office", ConditionOperator.Exists), Test("Office", ConditionOperator.Equals))),
            "when.parts[1].parts[1].value",
            "sequence.conditionValue");
        AssertOnlyAt(Test(MachineVariableNames.Model, ConditionOperator.Equals, " "), "when.value", "sequence.conditionValue");
    }

    [Fact]
    public void NamesTheFieldInAnIfsTestAndARepeatsUntil()
    {
        IfStep branch = If([Reboot()]) with { Test = Any(Test(MachineVariableNames.TpmPresent, ConditionOperator.Contains, "true")) };
        RepeatStep repeat = Repeat(Reboot()) with { Until = Test(MachineVariableNames.LastExitCode, ConditionOperator.Equals, "zero") };

        AssertOnly(Validate(Partition(), branch), branch, "test.parts[0].operator", "sequence.operatorDoesNotFit");
        AssertOnly(Validate(Partition(), repeat), repeat, "until.value", "sequence.conditionNumber");
    }

    [Theory]
    [InlineData(MachineVariableNames.MemoryMegabytes, ConditionOperator.Contains, "Number")]
    [InlineData(MachineVariableNames.TpmPresent, ConditionOperator.Greater, "YesNo")]
    [InlineData(MachineVariableNames.Model, ConditionOperator.Greater, "Text")]
    [InlineData(MachineVariableNames.Subnet, ConditionOperator.InSubnet, "Text")]
    [InlineData(MachineVariableNames.IPv4Address, ConditionOperator.Contains, "IPv4")]
    [InlineData(MachineVariableNames.MacAddress, ConditionOperator.Matches, "Mac")]
    [InlineData(MachineVariableNames.LastStepFailed, ConditionOperator.In, "YesNo")]
    public void RefusesAnOperatorThatDoesNotFitTheFactsType(string variable, ConditionOperator op, string type)
    {
        IReadOnlyList<SequenceProblem> problems = ValidateWhen(Test(variable, op, "1"), out RebootStep step);

        AssertOnly(problems, step, "when.operator", "sequence.operatorDoesNotFit");
        Assert.Equal(type, Assert.Single(problems).Args!["type"]);
    }

    [Theory]
    [InlineData(MachineVariableNames.MemoryMegabytes, ConditionOperator.Greater, "lots", "sequence.conditionNumber")]
    [InlineData(MachineVariableNames.MemoryMegabytes, ConditionOperator.In, "8192;many", "sequence.conditionNumber")]
    [InlineData(MachineVariableNames.TpmPresent, ConditionOperator.Equals, "maybe", "sequence.conditionYesNo")]
    [InlineData(MachineVariableNames.IPv4Address, ConditionOperator.Equals, "10.0.0", "sequence.conditionIPv4")]
    [InlineData(MachineVariableNames.DhcpServer, ConditionOperator.In, "10.0.0.1;10.0.0.256", "sequence.conditionIPv4")]
    [InlineData(MachineVariableNames.IPv4Address, ConditionOperator.InSubnet, "10.0.0.0", "sequence.conditionSubnet")]
    [InlineData(MachineVariableNames.MacAddress, ConditionOperator.Equals, "00:15:5D", "mac.enterFull")]
    [InlineData(MachineVariableNames.MacAddress, ConditionOperator.In, "00:15:5D:01:02:03;00:15", "mac.enterFull")]
    [InlineData(MachineVariableNames.PrimaryMacAddress, ConditionOperator.EndsWith, "zz", "mac.enterPart")]
    [InlineData(MachineVariableNames.Model, ConditionOperator.In, ";;", "sequence.conditionValue")]
    public void RefusesAValueThatIsNotOfTheFactsType(string variable, ConditionOperator op, string value, string code) =>
        AssertOnlyAt(Test(variable, op, value), "when.value", code);

    [Fact]
    public void NeedsNoValueToTestWhetherThereIsOne()
    {
        Assert.Empty(ValidateWhen(Test(MachineVariableNames.AssetTag, ConditionOperator.Exists), out _));
        Assert.Empty(ValidateWhen(Test("Office", ConditionOperator.NotExists), out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1st")]
    [InlineData("Site name")]
    public void RefusesSomethingToTestThatIsNoName(string variable) =>
        AssertOnlyAt(Test(variable, ConditionOperator.Equals, "x"), "when.variable", "sequence.conditionName");

    [Fact]
    public void RefusesAnOperatorItDoesNotKnow() =>
        AssertOnlyAt(Test(MachineVariableNames.Model, (ConditionOperator)99, "x"), "when.operator", "sequence.conditionOperator");

    // An Account input's answer is a password, which is never a value to compare.
    [Fact]
    public void RefusesToTestAnAccountInput() =>
        AssertOnlyAt(Test("installer", ConditionOperator.Exists), "when.variable", "sequence.accountInputAsValue");

    [Fact]
    public void RefusesAnEmptyPart() =>
        AssertOnlyAt(Any(Test("Office", ConditionOperator.Exists), null!), "when.parts[1]", "sequence.conditionEmpty");

    [Fact]
    public void NestsGroupsAtMostFourDeep()
    {
        TestCondition office = Test("Office", ConditionOperator.Exists);

        Assert.Empty(ValidateWhen(All(Any(None(All(office)))), out _));
        AssertOnlyAt(All(Any(None(All(Any(office))))), "when.parts[0].parts[0].parts[0].parts[0]", "sequence.conditionTooDeep");
    }

    [Fact]
    public void TestsAtMostTwentyThingsInANodesConditions()
    {
        TestCondition office = Test("Office", ConditionOperator.Exists);
        StepCondition phase = new(MachineVariableNames.Phase, ConditionOperator.Equals, "WindowsPE");

        Assert.Empty(ValidateWhen(All([.. Enumerable.Repeat(office, SequenceValidator.MaxTestsPerNode)]), out _));
        AssertOnlyAt(All([.. Enumerable.Repeat(office, SequenceValidator.MaxTestsPerNode + 1)]), "when", "sequence.tooManyTests");

        // The step's version 1 conditions count too.
        RebootStep both = Reboot() with
        {
            Conditions = [.. Enumerable.Repeat(phase, SequenceValidator.MaxConditions)],
            When = All([.. Enumerable.Repeat(office, SequenceValidator.MaxTestsPerNode - SequenceValidator.MaxConditions + 1)]),
        };
        AssertOnly(
            Validate(Definition(Partition(), both) with { Variables = [new VariableDeclaration { Name = "Office" }] }),
            both,
            "when",
            "sequence.tooManyTests");
    }

    // The legacy evaluator only knows version 1's operators and never holds for anything else. So a document has to
    // use When for the others.
    [Fact]
    public void KeepsTheConditionsOfVersionOneToItsOperators()
    {
        RebootStep reboot = Reboot() with { Conditions = [new StepCondition(MachineVariableNames.Model, ConditionOperator.EndsWith, "40")] };

        AssertOnly(Validate(Partition(), reboot), reboot, "conditions[0].operator", "sequence.conditionOperator");
    }
}
