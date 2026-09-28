// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Server.Deployments;
using Xunit;

namespace DDT.Server.Tests;

public sealed class RunSnapshotSizeTests
{
    private const long Megabyte = 1024L * 1024;

    // Partitions, an image both downloaded and applied, two driver packages in one step, and a raw disk image followed
    // by the seed. The raw disk image only counts the disk it holds. The sum is what it always was.
    [Fact]
    public void AddsUpAFlatSequenceAsBefore()
    {
        PartitionStep partition = new() { Id = Guid.NewGuid(), Name = "Partition", SystemPartitionMegabytes = 260, RecoveryPartitionMegabytes = 2048 };
        ApplyImageStep apply = new() { Id = Guid.NewGuid(), Name = "Apply", ImageId = Guid.NewGuid() };
        InjectDriversStep drivers = new() { Id = Guid.NewGuid(), Name = "Drivers" };
        WriteRawImageStep raw = new() { Id = Guid.NewGuid(), Name = "Raw", ImageId = Guid.NewGuid() };
        WriteCloudInitSeedStep seed = new() { Id = Guid.NewGuid(), Name = "Seed", MetaData = "", UserData = "" };
        SequenceDefinition definition = new(2, [partition, apply, drivers, raw, seed]);

        long required = RunSnapshots.RequiredBytes(
            definition,
            [
                Artifact(apply.Id, ArtifactKind.Image, 4000 * Megabyte, 9000 * Megabyte),
                Artifact(drivers.Id, ArtifactKind.Drivers, 300 * Megabyte, 600 * Megabyte),
                Artifact(drivers.Id, ArtifactKind.Drivers, 100 * Megabyte, 200 * Megabyte),
                Artifact(raw.Id, ArtifactKind.Image, 2000 * Megabyte, 3500 * Megabyte),
            ]);

        Assert.Equal(
            ((260 + 2048 + 16) * Megabyte) + (13000 * Megabyte) + (1200 * Megabyte) + (3500 * Megabyte) + CloudInitSeed.DiskBytes,
            required);
    }

    // A different image per model. The run needs room for the larger one.
    [Fact]
    public void TakesTheLargerImageOfTwoBranches()
    {
        ApplyImageStep small = new() { Id = Guid.NewGuid(), Name = "Small", ImageId = Guid.NewGuid() };
        ApplyImageStep large = new() { Id = Guid.NewGuid(), Name = "Large", ImageId = Guid.NewGuid() };
        PartitionStep partition = new() { Id = Guid.NewGuid(), Name = "Partition" };
        IfStep choose = new()
        {
            Id = Guid.NewGuid(),
            Name = "If a ThinkPad",
            Test = new TestCondition(MachineVariableNames.FriendlyModel, ConditionOperator.Matches, "ThinkPad*"),
            Then = [small],
            Else = [large],
        };

        long required = RunSnapshots.RequiredBytes(
            new SequenceDefinition(SequenceDefinition.CurrentVersion, [partition, choose]),
            [
                Artifact(small.Id, ArtifactKind.Image, 3000 * Megabyte, 8000 * Megabyte),
                Artifact(large.Id, ArtifactKind.Image, 5000 * Megabyte, 12000 * Megabyte),
            ]);

        Assert.Equal(
            (((long)partition.SystemPartitionMegabytes + partition.RecoveryPartitionMegabytes + 16) * Megabyte) + (17000 * Megabyte),
            required);
    }

    private static DeploymentArtifact Artifact(Guid stepId, ArtifactKind kind, long size, long expanded) => new()
    {
        StepId = stepId,
        Kind = kind,
        Name = "file",
        Sha256 = "",
        SizeBytes = size,
        ExpandedBytes = expanded,
    };
}
