// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class DryRunDiskPartitionerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ddt-dry-run-test-{Guid.NewGuid():N}");
    private readonly DryRunDiskPartitioner _partitioner;

    public DryRunDiskPartitionerTests()
    {
        _partitioner = new DryRunDiskPartitioner(_root, new AgentLog(new ImmediateTimeProvider(), TextWriter.Null));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task PartitionsIntoDirectoriesWithFixedIds()
    {
        TargetVolumes volumes = await _partitioner.PartitionAsync(FakeDeploymentTools.Disk(0), 300, 1024, TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(_root, "W"), volumes.Windows);
        Assert.True(Directory.Exists(volumes.System) && Directory.Exists(volumes.Windows) && Directory.Exists(volumes.Recovery));
        Assert.Equal(
            (DryRunDiskPartitioner.SystemPartitionId, DryRunDiskPartitioner.WindowsPartitionId, DryRunDiskPartitioner.RecoveryPartitionId),
            (volumes.SystemPartitionId, volumes.WindowsPartitionId, volumes.RecoveryPartitionId));
    }

    [Fact]
    public async Task FindsItsDirectoriesAgainWithoutTouchingThem()
    {
        TargetVolumes partitioned = await _partitioner.PartitionAsync(FakeDeploymentTools.Disk(0), 300, 1024, TestContext.Current.CancellationToken);
        string kept = Path.Combine(partitioned.Windows, "DDT", "run", "state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
        await File.WriteAllTextAsync(kept, "{}", TestContext.Current.CancellationToken);
        Guid erased = Guid.Parse("5a5a0000-0000-4000-8000-0000000000e0");

        TargetVolumes found = await _partitioner.FindAsync(
            new RunDiskIds(partitioned.SystemPartitionId, partitioned.WindowsPartitionId, partitioned.RecoveryPartitionId, [erased]),
            partitioned.Windows,
            TestContext.Current.CancellationToken);

        Assert.Equal((partitioned.System, partitioned.Windows, partitioned.Recovery), (found.System, found.Windows, found.Recovery));
        Assert.Equal([erased], found.ErasedSystemPartitionIds);
        Assert.True(File.Exists(kept));
    }

    [Fact]
    public async Task RefusesAWindowsPartitionItDidNotMake()
    {
        TargetVolumes partitioned = await _partitioner.PartitionAsync(FakeDeploymentTools.Disk(0), 300, 1024, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<DeploymentStepException>(() => _partitioner.FindAsync(
            new RunDiskIds(partitioned.SystemPartitionId, Guid.Parse("11111111-2222-4333-8444-555555555555"), partitioned.RecoveryPartitionId, []),
            partitioned.Windows,
            TestContext.Current.CancellationToken));
    }
}
