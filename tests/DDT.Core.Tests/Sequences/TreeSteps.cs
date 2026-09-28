// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Core.Tests.Sequences;

// Nodes for the validator's tests of trees, each with a new id, and the checks they share.
internal static class TreeSteps
{
    public static TestCondition ThinkPad { get; } = new(MachineVariableNames.FriendlyModel, ConditionOperator.Matches, "ThinkPad*");

    public static SequenceDefinition Definition(params SequenceStep[] steps) => new(SequenceDefinition.CurrentVersion, steps);

    public static IReadOnlyList<SequenceProblem> Validate(params SequenceStep[] steps) => SequenceValidator.Validate(Definition(steps));

    public static IReadOnlyList<SequenceProblem> Validate(SequenceDefinition definition) => SequenceValidator.Validate(definition);

    // Each problem as where it is and what it says, which is what a test compares.
    public static IEnumerable<(Guid? StepId, string? Field, string? Code)> Said(IEnumerable<SequenceProblem> problems) =>
        problems.Select(problem => (problem.StepId, problem.Field, problem.Code));

    public static void AssertOnly(IReadOnlyList<SequenceProblem> problems, SequenceStep? step, string? field, string code)
    {
        SequenceProblem problem = Assert.Single(problems);
        Assert.Equal((step?.Id, field, code), (problem.StepId, problem.Field, problem.Code));
    }

    public static PartitionStep Partition() => new() { Id = Guid.NewGuid(), Name = "Partition the disk" };

    public static ApplyImageStep ApplyImage() => new() { Id = Guid.NewGuid(), Name = "Apply the image", ImageId = Guid.NewGuid() };

    public static InjectDriversStep InjectDrivers() => new() { Id = Guid.NewGuid(), Name = "Add drivers" };

    public static WriteUnattendStep WriteUnattend() => new() { Id = Guid.NewGuid(), Name = "Write the answer file" };

    public static JoinDomainStep JoinDomain() => new() { Id = Guid.NewGuid(), Name = "Join the domain" };

    public static RunScriptStep Script(SequencePhase phase) =>
        new() { Id = Guid.NewGuid(), Name = "Run a script", Phase = phase, Script = "exit /b 0", RebootExitCodes = [] };

    public static RebootStep Reboot() => new() { Id = Guid.NewGuid(), Name = "Restart" };

    public static WriteRawImageStep WriteRawImage() => new() { Id = Guid.NewGuid(), Name = "Write the raw image", ImageId = Guid.NewGuid() };

    public static WriteCloudInitSeedStep WriteSeed() =>
        new() { Id = Guid.NewGuid(), Name = "Write the seed", MetaData = "instance-id: a", UserData = "#cloud-config" };

    public static PauseStep Pause(string message = "Check the BIOS.") => new() { Id = Guid.NewGuid(), Name = "Pause", Message = message };

    public static SetVariableStep SetVariable(string variable, string value) =>
        new() { Id = Guid.NewGuid(), Name = "Set a variable", Variable = variable, Value = value };

    public static GroupStep Group(params SequenceStep[] steps) => new() { Id = Guid.NewGuid(), Name = "Group", Steps = steps };

    public static IfStep If(IReadOnlyList<SequenceStep> then, IReadOnlyList<SequenceStep>? otherwise = null) =>
        new() { Id = Guid.NewGuid(), Name = "If a ThinkPad", Test = ThinkPad, Then = then, Else = otherwise ?? [] };

    public static RepeatStep Repeat(params SequenceStep[] steps) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Until it works",
        Steps = steps,
        Until = new TestCondition(MachineVariableNames.LastExitCode, ConditionOperator.Equals, "0"),
    };
}
