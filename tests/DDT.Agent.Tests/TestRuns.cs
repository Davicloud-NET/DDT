// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;

namespace DDT.Agent.Tests;

// Steps and runs as the server sends them. InstallWindows is what an image deployment did before task sequences.
internal static class TestRuns
{
    public static readonly Guid RunId = Guid.Parse("0193a4b2-0000-7000-8000-0000000000f1");

    public static PartitionStep Partition { get; } = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000b001"), Name = "Partition" };

    public static ApplyImageStep Apply { get; } = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000b002"),
        Name = "Apply image",
        ImageId = TestImage.ImageId,
    };

    public static WriteUnattendStep Unattend { get; } = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000b003"), Name = "Answer file" };

    public static RebootStep Reboot { get; } = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000b004"), Name = "Restart" };

    public static IReadOnlyList<SequenceStep> InstallWindows { get; } = [Partition, Apply, Unattend];

    // A cmd script in Windows PE whose id ends in number.
    public static RunScriptStep Script(int number, SequencePhase phase = SequencePhase.WindowsPE, ScriptInterpreter interpreter = ScriptInterpreter.Cmd) => new()
    {
        Id = Guid.Parse($"0193a4b2-0000-7000-8000-00000000c{number:D3}"),
        Name = $"Script {number}",
        Phase = phase,
        Interpreter = interpreter,
        Script = $"echo {number}",
    };

    public static AgentRun Run(
        IReadOnlyList<SequenceStep> steps,
        TestImage? image = null,
        DeploymentState state = DeploymentState.Assigned,
        int? diskNumber = null,
        IReadOnlyList<AgentRunPackage>? packages = null) =>
        new(
            RunId,
            state,
            "Install Windows",
            new SequenceDefinition(SequenceDefinition.CurrentVersion, steps),
            image is null ? [] : [image.RunImage],
            packages ?? [],
            diskNumber,
            "PC-042");
}
