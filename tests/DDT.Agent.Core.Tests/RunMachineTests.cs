// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class RunMachineTests
{
    private static readonly MachineIdentity s_latitude = new(
        "4c4c4544-0042-3510-8052-b4c04f4d3232",
        "00155D0A0B0C",
        ["00155D010203", "00155D0A0B0C"],
        "LENOVO",
        "21HD",
        "PF3ABCDE",
        SecureBootEnabled: true,
        ChassisType: 10,
        Facts: new MachineFacts
        {
            MemoryMegabytes = 16384,
            SystemVersion = "ThinkPad T14 Gen 4",
            SystemSku = "Default string",
            AssetTag = "To Be Filled By O.E.M.",
            BaseboardProduct = "21HDCTO1WW",
            IPv4Address = "10.0.4.20",
            IPv4PrefixLength = 22,
        });

    // Conditions and templates read the facts the agent registered with and the values the run started with.
    [Fact]
    public void ReadsTheFactsTheMachineRegisteredWithAndTheRunsValues()
    {
        AgentRun run = TestRuns.Run([TestRuns.Script(1)]) with
        {
            Values = new Dictionary<string, string> { ["Office"] = "Vienna", [MachineVariableNames.ComputerName] = "VIE-042" },
        };

        MachineVariables machine = RunMachine.Of(s_latitude, run);

        Assert.Equal(
            ["ThinkPad T14 Gen 4", "Laptop", "00155D0A0B0C", "true", "16384", "10.0.4.0/22", "21HDCTO1WW", "Vienna", "VIE-042"],
            [
                machine.Value(MachineVariableNames.FriendlyModel),
                machine.Value(MachineVariableNames.DeviceKind),
                machine.Value(MachineVariableNames.PrimaryMacAddress),
                machine.Value(MachineVariableNames.SecureBootEnabled),
                machine.Value(MachineVariableNames.MemoryMegabytes),
                machine.Value(MachineVariableNames.Subnet),
                machine.Value(MachineVariableNames.BaseboardProduct),
                machine.Value("office"),
                machine.Value(MachineVariableNames.ComputerName),
            ]);
        Assert.Equal(SequencePhase.WindowsPE, machine.Phase);
    }

    // The server drops a board maker's placeholder at registration, so a condition on the agent must not see one either.
    [Fact]
    public void LeavesOutWhatTheFirmwareLeftAsAPlaceholder()
    {
        MachineVariables machine = RunMachine.Of(s_latitude, TestRuns.Run([TestRuns.Script(1)]));

        Assert.Empty(machine.Values(MachineVariableNames.SystemSku));
        Assert.Empty(machine.Values(MachineVariableNames.AssetTag));
        Assert.False(ConditionEvaluator.Holds(new TestCondition(MachineVariableNames.AssetTag, ConditionOperator.Exists), machine));
        Assert.Equal("PC-042", machine.Value(MachineVariableNames.ComputerName));
    }
}
