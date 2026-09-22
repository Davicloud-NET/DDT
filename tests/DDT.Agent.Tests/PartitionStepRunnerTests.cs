// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class PartitionStepRunnerTests : IDisposable
{
    private static readonly PartitionStep s_step = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a1"),
        Name = "Partition",
        SystemPartitionMegabytes = 500,
        RecoveryPartitionMegabytes = 2048,
    };

    private readonly StepRunnerFixture _run = new([s_step]);

    public void Dispose() => _run.Dispose();

    [Fact]
    public async Task PartitionsTheChosenDiskWithTheStepsSizes()
    {
        _run.Session.Disk = FakeDeploymentTools.Disk(2);

        StepResult result = await _run.Partition.RunAsync(s_step, _run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Done, result.Outcome);
        Assert.Equal(["partition 2"], _run.Tools.Calls);
        Assert.Equal((500, 2048), _run.Tools.PartitionSizes);
        Assert.Equal(_run.Tools.Volumes.Windows, _run.Session.Volumes?.Windows);
        Assert.Equal(FakeDeploymentTools.WindowsPartitionId, _run.Session.Volumes?.WindowsPartitionId);
        Assert.Equal(100, _run.Progress.Values[^1]);
    }

    [Fact]
    public async Task OutputsTheIdsThatFindThePartitionsAgain()
    {
        StepResult result = await _run.Partition.RunAsync(s_step, _run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(
            new Dictionary<string, string>
            {
                [RunVariables.SystemPartition] = FakeDeploymentTools.SystemPartitionId.ToString("D"),
                [RunVariables.WindowsPartition] = FakeDeploymentTools.WindowsPartitionId.ToString("D"),
                [RunVariables.RecoveryPartition] = FakeDeploymentTools.RecoveryPartitionId.ToString("D"),
                [RunVariables.ErasedSystemPartitions] = FakeDeploymentTools.ErasedSystemPartitionId.ToString("D"),
            },
            result.Outputs);
    }

    [Fact]
    public async Task GivesTheRunItsDirectoryOnTheWindowsVolume()
    {
        await _run.Partition.RunAsync(s_step, _run.Context(), TestContext.Current.CancellationToken);

        string directory = Path.Combine(_run.Tools.Volumes.Windows, "DDT");
        Assert.Equal(directory, _run.Session.RunDirectory);
        Assert.True(Directory.Exists(directory));
        Assert.Contains(
            await _run.SentLinesAsync(),
            line => line.Message == $"Dry run: {directory} is left open. In Windows PE only SYSTEM could open it ({SystemOnlyDirectory.Sddl}).");
    }

    [Fact]
    public async Task PartitionsNothingWithoutAChosenDisk()
    {
        _run.Session.Disk = null;

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(
            () => _run.Partition.RunAsync(s_step, _run.Context(), TestContext.Current.CancellationToken));

        Assert.Equal("No disk was chosen for this run, so nothing was partitioned.", exception.Message);
        Assert.Empty(_run.Tools.Calls);
    }
}
