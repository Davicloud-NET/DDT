// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Sequences;

// The run variables the agent's steps output. They are kept in the run's state, so they survive restarts, and they
// start with "ddt." so they never meet a name a sequence author chooses.
public static class RunVariables
{
    // The unique GUIDs of the new system, Windows and recovery partitions, and those of the EFI system partitions
    // the partitioning erased, separated by commas.
    public const string SystemPartition = "ddt.disk.system";
    public const string WindowsPartition = "ddt.disk.windows";
    public const string RecoveryPartition = "ddt.disk.recovery";
    public const string ErasedSystemPartitions = "ddt.disk.erased-esps";

    // "1" once the image is on the disk, which then has to be made bootable before the run ends.
    public const string WindowsApplied = "ddt.windows-applied";

    // How often Windows PE started after the run was handed over to the installed Windows, which it then hands over
    // again.
    public const string WindowsPEReturns = "ddt.winpe-returns";

    public const string Set = "1";

    public static IReadOnlyDictionary<string, string> Of(TargetVolumes volumes)
    {
        ArgumentNullException.ThrowIfNull(volumes);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SystemPartition] = volumes.SystemPartitionId.ToString("D"),
            [WindowsPartition] = volumes.WindowsPartitionId.ToString("D"),
            [RecoveryPartition] = volumes.RecoveryPartitionId.ToString("D"),
            [ErasedSystemPartitions] = string.Join(',', volumes.ErasedSystemPartitionIds.Select(id => id.ToString("D"))),
        };
    }

    // What Of wrote, or null when the variables do not hold all of it, as before Partition.
    public static RunDiskIds? DiskIds(IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        if (!TryGetId(variables, SystemPartition, out Guid system)
            || !TryGetId(variables, WindowsPartition, out Guid windows)
            || !TryGetId(variables, RecoveryPartition, out Guid recovery)
            || !variables.TryGetValue(ErasedSystemPartitions, out string? erasedList))
        {
            return null;
        }

        List<Guid> erased = [];

        foreach (string value in erasedList.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Guid.TryParse(value, out Guid id))
            {
                return null;
            }

            erased.Add(id);
        }

        return new RunDiskIds(system, windows, recovery, erased);
    }

    private static bool TryGetId(IReadOnlyDictionary<string, string> variables, string name, out Guid id)
    {
        id = Guid.Empty;

        return variables.TryGetValue(name, out string? value) && Guid.TryParse(value, out id);
    }
}
