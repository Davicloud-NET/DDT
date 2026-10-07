// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class RunVariablesTests
{
    private static readonly Guid s_system = Guid.Parse("5a5a0000-0000-4000-8000-000000000001");
    private static readonly Guid s_windows = Guid.Parse("5a5a0000-0000-4000-8000-000000000002");
    private static readonly Guid s_recovery = Guid.Parse("5a5a0000-0000-4000-8000-000000000003");
    private static readonly Guid s_erased = Guid.Parse("5a5a0000-0000-4000-8000-0000000000e0");

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TheDiskIdsComeBackFromTheVariables(int erasedCount)
    {
        Guid[] erased = [.. Enumerable.Repeat(s_erased, erasedCount)];
        TargetVolumes volumes = new(@"S:\", @"W:\", @"R:\", erased)
        {
            SystemPartitionId = s_system,
            WindowsPartitionId = s_windows,
            RecoveryPartitionId = s_recovery,
        };

        RunDiskIds? ids = RunVariables.DiskIds(RunVariables.Of(volumes));

        Assert.NotNull(ids);
        Assert.Equal((s_system, s_windows, s_recovery), (ids.System, ids.Windows, ids.Recovery));
        Assert.Equal(erased, ids.ErasedSystemPartitionIds);
    }

    [Fact]
    public void BeforePartitionThereAreNoDiskIds()
    {
        Assert.Null(RunVariables.DiskIds(new Dictionary<string, string>()));
        Assert.Null(RunVariables.DiskIds(new Dictionary<string, string>
        {
            [RunVariables.SystemPartition] = s_system.ToString(),
            [RunVariables.WindowsPartition] = "not a guid",
            [RunVariables.RecoveryPartition] = s_recovery.ToString(),
            [RunVariables.ErasedSystemPartitions] = string.Empty,
        }));
    }
}
