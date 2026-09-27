// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

// A disk DDT could install on. Number is the N of \\.\PhysicalDriveN, which is also diskpart's disk number.
public sealed record LocalDisk(int Number, string? Model, long SizeBytes, StorageBusType BusType, int PartitionCount)
{
    public string DisplayModel => DisplayModelOf(Model);

    public AgentDisk ToAgentDisk() => new(Number, Model, SizeBytes, BusType.ToString(), PartitionCount);

    public ConsoleDisk ToConsoleDisk() => new(Number, Model, SizeBytes, BusType.ToString(), PartitionCount);

    // The partition count is left out: it describes what is on the disk, not which disk it is.
    public bool IsSameDiskAs(LocalDisk other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Number == other.Number && Model == other.Model && SizeBytes == other.SizeBytes && BusType == other.BusType;
    }

    public string Describe() => Describe(Number, Model, SizeBytes, BusType.ToString(), PartitionCount);

    // As the text console lists a disk, which it knows only as the console protocol describes it.
    public static string Describe(int number, string? model, long sizeBytes, string busType, int partitionCount) =>
        $"Disk {number}: {DisplayModelOf(model)}, {ByteSize.Format(sizeBytes)}, {busType}, {Partitions(partitionCount)}";

    public static string DisplayModelOf(string? model) => model ?? "unknown model";

    public static string Partitions(int count) => count == 1 ? "1 partition" : $"{count} partitions";
}
