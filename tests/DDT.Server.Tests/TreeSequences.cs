// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Server.Tests;

// Nodes of the sequences used to test tree runs, each with a new ID, and the visits an agent reports for them.
internal static class TreeSequences
{
    public static TestCondition ThinkPad { get; } = new(MachineVariableNames.FriendlyModel, ConditionOperator.Matches, "ThinkPad*");

    public static PartitionStep Partition() => new() { Id = Guid.NewGuid(), Name = "Partition" };

    public static ApplyImageStep Apply(Guid imageId, string name = "Apply") => new() { Id = Guid.NewGuid(), Name = name, ImageId = imageId };

    public static RunScriptStep Script(string name, SequencePhase phase = SequencePhase.WindowsPE) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Phase = phase,
        Interpreter = ScriptInterpreter.Cmd,
        Script = "exit /b 0",
        RebootExitCodes = [],
    };

    public static GroupStep Group(string name, params SequenceStep[] steps) => new() { Id = Guid.NewGuid(), Name = name, Steps = steps };

    public static IfStep If(IReadOnlyList<SequenceStep> then, IReadOnlyList<SequenceStep> otherwise) =>
        new() { Id = Guid.NewGuid(), Name = "If a ThinkPad", Test = ThinkPad, Then = then, Else = otherwise };

    public static RepeatStep Repeat(params SequenceStep[] steps) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Until it works",
        Steps = steps,
        Until = new TestCondition(MachineVariableNames.LastExitCode, ConditionOperator.Equals, "0"),
        MaxTimes = 3,
    };

    public static PauseStep Pause(string message = "Check the BIOS of {{SerialNumber}}.") =>
        new() { Id = Guid.NewGuid(), Name = "Check the BIOS", Message = message };

    public static StepRunState Visit(SequenceStep node, StepState state, int pass = 1, string? error = null) => new(node.Id, state, error, pass);
}
