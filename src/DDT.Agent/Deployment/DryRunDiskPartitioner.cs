// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;

namespace DDT.Agent.Deployment;

// Stands in for a disk with three directories under root, which the runner deletes when the run ends. Its partitions
// always have the same ids, SystemPartitionId, WindowsPartitionId and RecoveryPartitionId.
public sealed class DryRunDiskPartitioner(string root, AgentLog log) : IDiskPartitioner
{
    public static readonly Guid SystemPartitionId = Guid.Parse("d7c1a5e0-0000-4000-8000-000000000001");
    public static readonly Guid WindowsPartitionId = Guid.Parse("d7c1a5e0-0000-4000-8000-000000000002");
    public static readonly Guid RecoveryPartitionId = Guid.Parse("d7c1a5e0-0000-4000-8000-000000000003");

    private static readonly LocalDisk s_disk = new(0, "Dry run disk", 128L * 1024 * 1024 * 1024, StorageBusType.Virtual, 0);

    public Task<IReadOnlyList<LocalDisk>> ListDisksAsync(CancellationToken cancellationToken)
    {
        log.Information($"Dry run: {s_disk.Describe()} stands in for this computer's disks.");

        return Task.FromResult<IReadOnlyList<LocalDisk>>([s_disk]);
    }

    public async Task<TargetVolumes> PartitionAsync(
        LocalDisk disk,
        int systemPartitionMegabytes,
        int recoveryPartitionMegabytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(disk);

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        TargetVolumes volumes = Volumes();
        Directory.CreateDirectory(volumes.System);
        Directory.CreateDirectory(volumes.Windows);
        Directory.CreateDirectory(volumes.Recovery);

        string script = DiskpartScript.Build(disk.Number, 'S', 'W', 'R', systemPartitionMegabytes, recoveryPartitionMegabytes);
        await File.WriteAllTextAsync(Path.Combine(root, "partition.txt"), script, Encoding.ASCII, cancellationToken)
            .ConfigureAwait(false);

        log.Information($"Dry run: diskpart is not run. It would get this script for disk {disk.Number}:");

        foreach (string line in script.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            log.Information($"  {line}");
        }

        log.Information($"Dry run: the new volumes are the directories under {root}.");

        return volumes;
    }

    private TargetVolumes Volumes() =>
        new(Path.Combine(root, "S"), Path.Combine(root, "W"), Path.Combine(root, "R"), [])
        {
            SystemPartitionId = SystemPartitionId,
            WindowsPartitionId = WindowsPartitionId,
            RecoveryPartitionId = RecoveryPartitionId,
        };
}
