// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Core.Tests.Sequences;

public sealed class SequenceSizesTests
{
    private const long Gigabyte = 1024L * 1024 * 1024;
    private const long Megabyte = 1024L * 1024;

    private static readonly TestCondition s_lenovo = new(MachineVariableNames.Manufacturer, ConditionOperator.Equals, "LENOVO");

    // The files each step puts on the disk, by the step's name.
    private static readonly Dictionary<string, long> s_files = new(StringComparer.Ordinal)
    {
        ["Small image"] = 10 * Gigabyte,
        ["Large image"] = 20 * Gigabyte,
        ["Drivers"] = 2 * Gigabyte,
        ["Script"] = 100 * Megabyte,

        // Less than the disk space the seed needs.
        ["Tiny"] = 10 * Megabyte,
        ["Raw"] = 8 * Gigabyte,
    };

    [Fact]
    public void AddsUpAFlatSequenceAsBefore()
    {
        PartitionStep partition = new() { Id = Guid.NewGuid(), Name = "Partition", SystemPartitionMegabytes = 300, RecoveryPartitionMegabytes = 1024 };
        List<SequenceStep> steps =
        [
            partition,
            Leaf("Small image"),
            Leaf("Drivers") with { Conditions = [new StepCondition(MachineVariableNames.Model, ConditionOperator.StartsWith, "Latitude")] },
            Leaf("Script"),
            new RebootStep { Id = Guid.NewGuid(), Name = "Restart" },
        ];

        long required = Required(steps);

        Assert.Equal((300 + 1024 + 16) * Megabyte + (10 * Gigabyte) + (2 * Gigabyte) + (100 * Megabyte), required);
        Assert.Equal((300 + 1024) * Megabyte + SequenceSizes.ReservedPartitionBytes, SequenceSizes.PartitionBytes(partition));
    }

    [Fact]
    public void CountsTheSeedOnceOnAFlatSequence()
    {
        long required = Required([Leaf("Raw"), Seed(), Seed()]);

        Assert.Equal((8 * Gigabyte) + CloudInitSeed.DiskBytes, required);
        Assert.Equal(0, Required([]));
    }

    [Fact]
    public void TakesTheLargerBranchOfAnIf()
    {
        long required = Required(
        [
            new PartitionStep { Id = Guid.NewGuid(), Name = "Partition" },
            If([Leaf("Small image")], [Leaf("Large image")]),
            Leaf("Script"),
        ]);

        Assert.Equal(Partition() + (20 * Gigabyte) + (100 * Megabyte), required);
        Assert.Equal(Partition() + (20 * Gigabyte), Required([new PartitionStep { Id = Guid.NewGuid(), Name = "Partition" }, If([Leaf("Large image")], [])]));
    }

    // A group adds up its steps. An else-if is an IF in Else. A repeat's body counts once.
    [Fact]
    public void WalksNestedContainers()
    {
        long required = Required(
        [
            If(
                [new GroupStep { Id = Guid.NewGuid(), Name = "Lenovo", Steps = [Leaf("Small image"), Leaf("Drivers")] }],
                [If([Leaf("Large image")], [Leaf("Small image")])]),
            new RepeatStep
            {
                Id = Guid.NewGuid(),
                Name = "Until it works",
                Until = s_lenovo,
                MaxTimes = 5,
                Steps = [Leaf("Script"), If([Leaf("Script")], [])],
            },
        ]);

        Assert.Equal((20 * Gigabyte) + (200 * Megabyte), required);
    }

    // A path that writes the seed counts the seed's disk space, and the path that needs the most wins.
    [Fact]
    public void CountsTheSeedOnlyOnThePathsThatWriteIt()
    {
        Assert.Equal(
            (8 * Gigabyte) + CloudInitSeed.DiskBytes,
            Required([If([Leaf("Raw"), Seed()], [Leaf("Raw"), Leaf("Tiny")])]));
        Assert.Equal(
            (20 * Gigabyte) + (100 * Megabyte),
            Required([If([Leaf("Raw"), Seed()], [Leaf("Large image")]), Leaf("Script")]));
        Assert.Equal(
            (8 * Gigabyte) + (10 * Megabyte) + CloudInitSeed.DiskBytes,
            Required([Leaf("Raw"), If([Seed()], [Leaf("Tiny")]), If([Leaf("Tiny")], [Seed()])]));
        Assert.Equal(
            (8 * Gigabyte) + CloudInitSeed.DiskBytes,
            Required([Leaf("Raw"), If([Seed()], []), Seed()]));
    }

    [Fact]
    public void PassesOverNullStepsAndBodies()
    {
        Assert.Equal(10 * Gigabyte, Required([null!, Leaf("Small image"), new GroupStep { Id = Guid.NewGuid(), Name = "Empty", Steps = null! }]));
    }

    private static long Required(IReadOnlyList<SequenceStep> steps) =>
        SequenceSizes.RequiredBytes(new SequenceDefinition(SequenceDefinition.CurrentVersion, steps), step => s_files.GetValueOrDefault(step.Name));

    private static long Partition() => SequenceSizes.PartitionBytes(new PartitionStep { Id = Guid.NewGuid(), Name = "Partition" });

    private static RunScriptStep Leaf(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Phase = SequencePhase.WindowsPE,
        Interpreter = ScriptInterpreter.Cmd,
        Script = "exit 0",
    };

    private static WriteCloudInitSeedStep Seed() => new() { Id = Guid.NewGuid(), Name = "Seed", MetaData = "", UserData = "" };

    private static IfStep If(IReadOnlyList<SequenceStep> then, IReadOnlyList<SequenceStep> otherwise) =>
        new() { Id = Guid.NewGuid(), Name = "If", Test = s_lenovo, Then = then, Else = otherwise };
}
