// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Deployment;
using DDT.Core.Disks;

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

    // "1" once a raw disk image is on the disk, which then gets a boot entry for its fallback file before the run ends.
    public const string RawImageWritten = "ddt.raw-written";

    // The raw disk image's EFI system partition: its entry number, first sector, sectors and unique GUID, separated by
    // commas. Missing when the image has none.
    public const string RawSystemPartition = "ddt.raw.esp";

    // The partition the cloud-init seed was written to, by its entry number.
    public const string SeedPartition = "ddt.seed.partition";

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

    // What a raw disk image leaves: that it is written, its EFI system partition in the table the disk has now, and the
    // EFI system partitions the clean erased.
    public static IReadOnlyDictionary<string, string> OfRawImage(GptLayout layout, IReadOnlyList<Guid> erased)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(erased);

        Dictionary<string, string> variables = new(StringComparer.Ordinal)
        {
            [RawImageWritten] = Set,
            [ErasedSystemPartitions] = string.Join(',', erased.Select(id => id.ToString("D"))),
        };

        if (layout.Partitions.FirstOrDefault(partition => partition.Type == GptPartitionTypes.EfiSystem) is { } esp)
        {
            variables[RawSystemPartition] = string.Create(CultureInfo.InvariantCulture, $"{esp.Number},{esp.FirstLba},{esp.Sectors},{esp.Id:D}");
        }

        return variables;
    }

    // The EFI system partition OfRawImage recorded, or null.
    public static EspPartition? RawSystemPartitionOf(IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        string[] parts = variables.GetValueOrDefault(RawSystemPartition)?.Split(',') ?? [];

        return parts.Length == 4
            && uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out uint number)
            && ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ulong first)
            && ulong.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out ulong sectors)
            && Guid.TryParse(parts[3], out Guid id)
                ? new EspPartition(number, first, sectors, id)
                : null;
    }

    // The EFI system partitions a clean or Partition erased, as far as the variables hold them.
    public static IReadOnlyList<Guid> ErasedSystemPartitionIdsOf(IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        return [.. (variables.GetValueOrDefault(ErasedSystemPartitions) ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => Guid.TryParse(value, out Guid id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)];
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
