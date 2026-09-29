// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Machines;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Core.Tests.Machines;

public sealed class HardwareModelsTests
{
    // Rules and driver targets from the server's tests, each with a machine it was tried on. Each case has the rule's
    // manufacturer and model, the machine's, and whether they match.
    public static TheoryData<string?, string, string?, string?, bool> Cases { get; } = new()
    {
        { "Dell Inc.", "Latitude 5440", "DELL INC.", "latitude 5440", true },
        { "DELL INC.", "OPTIPLEX 7010", "  dell   inc. ", "  optiplex   7010 ", true },
        { " Dell   Inc. ", "  OptiPlex  7* ", "Dell Inc.", "OptiPlex 7010", true },
        { null, "Virtual Machine", "Microsoft Corporation", "Virtual Machine", true },
        { "Microsoft Corporation", "Virtual*", "Microsoft Corporation", "Virtual Machine", true },
        { null, "Latitude 5440", "Microsoft Corporation", "Virtual Machine", false },
        { null, "To Be*", "To Be Filled By O.E.M.", "To Be Filled By O.E.M.", false },
        { null, "Precision*", "Dell Inc.", "Precision 5440", true },
        { null, "Precision 54*", "Dell Inc.", "Precision 5440", true },
        { "Dell Inc.", "Precision 54*", "Dell Inc.", "Precision 5440", true },
        { null, "Precision 5440", "Dell Inc.", "Precision 5440", true },
        { "Dell Inc.", "Precision 5440", "Dell Inc.", "Precision 5440", true },
        { "Lenovo", "Precision 5440", "Dell Inc.", "Precision 5440", false },
        { null, "Precision 5440 2-in-1", "Dell Inc.", "Precision 5440", false },
        { null, "Precision 5440", "Dell Inc.", "Precision 5440 2-in-1", false },
        { null, "Latitude 7*", "Dell Inc.", "Latitude 7440", true },
        { null, "Latitude*", "Dell Inc.", "Latitude", true },
        { "Dell Inc.", "Latitude 7*", null, "Latitude 7440", false },
        { null, "Latitude 7440", null, "Latitude 7440", true },
        { null, "Latitude 7440", "Dell Inc.", null, false },
        { "Microsoft Corporation", "Virtual Machine", "System manufacturer", "Virtual Machine", false },
        { null, "Virtual Machine", "System manufacturer", "Virtual Machine", true },
        { null, "Virtual Machine", "Microsoft Corporation", "System Product Name", false },
        { null, "Virtual*", "Microsoft Corporation", "Virtual", true },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatchesTheModelsTheServersTestsExpect(string? manufacturer, string model, string? machineManufacturer, string? machineModel, bool matches)
    {
        Assert.Equal(matches, HardwareModels.Matches(manufacturer, machineManufacturer) && HardwareModels.Matches(model, machineModel));
    }

    // The rule migration writes a model rule as all[Manufacturer Equals m, when it has one, Model Equals x or Matches
    // x*]. That must match exactly the machines the rule matched.
    [Theory]
    [MemberData(nameof(Cases))]
    public void AModelRuleAsAConditionMatchesWhatTheRuleMatched(
        string? manufacturer,
        string model,
        string? machineManufacturer,
        string? machineModel,
        bool matches)
    {
        MachineVariables machine = new(machineManufacturer, machineModel, "SN-1", "4c4c4544-0042-3510-8052-b4c04f4d3232", [], null, SequencePhase.WindowsPE);
        List<ConditionNode> parts = [];

        if (HardwareModels.Clean(manufacturer) is { } maker)
        {
            parts.Add(new TestCondition(MachineVariableNames.Manufacturer, ConditionOperator.Equals, maker));
        }

        parts.Add(new TestCondition(
            MachineVariableNames.Model,
            HardwareModels.IsPrefix(model) ? ConditionOperator.Matches : ConditionOperator.Equals,
            HardwareModels.Clean(model)!));

        Assert.Equal(matches, ConditionEvaluator.Holds(new AllCondition { Parts = parts }, machine));
    }

    [Fact]
    public void CleansAndRecognisesPlaceholders()
    {
        Assert.Equal("Dell Inc.", HardwareModels.Clean("  Dell \t  Inc. "));
        Assert.Null(HardwareModels.Clean("   "));
        Assert.Equal("DELL INC.", HardwareModels.Normalize(" dell  inc."));
        Assert.True(HardwareModels.IsPlaceholder(" to be filled by o.e.m. "));
        Assert.True(HardwareModels.IsPlaceholder("Default string"));
        Assert.True(HardwareModels.IsPlaceholder("System Version"));
        Assert.True(HardwareModels.IsPlaceholder("Chassis Asset Tag"));
        Assert.False(HardwareModels.IsPlaceholder("Dell Inc."));
        Assert.True(HardwareModels.IsPrefix("Latitude 7*"));
        Assert.True(HardwareModels.Matches(null, null));
    }
}
