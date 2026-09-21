// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;

namespace DDT.Agent.Deployment;

// Stands in for a disk with three directories under root, which the runner deletes when the run ends.
public sealed class DryRunDiskPartitioner(string root, AgentLog log) : IDiskPartitioner
{
    private static readonly LocalDisk s_disk = new(0, "Dry run disk", 128L * 1024 * 1024 * 1024, StorageBusType.Virtual, 0);

    public Task<IReadOnlyList<LocalDisk>> ListDisksAsync(CancellationToken cancellationToken)
    {
        log.Information($"Dry run: {s_disk.Describe()} stands in for this computer's disks.");

        return Task.FromResult<IReadOnlyList<LocalDisk>>([s_disk]);
    }

    public async Task<TargetVolumes> PartitionAsync(LocalDisk disk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(disk);

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        TargetVolumes volumes = new(Path.Combine(root, "S"), Path.Combine(root, "W"), Path.Combine(root, "R"), []);
        Directory.CreateDirectory(volumes.System);
        Directory.CreateDirectory(volumes.Windows);
        Directory.CreateDirectory(volumes.Recovery);

        string script = DiskpartScript.Build(disk.Number, 'S', 'W', 'R');
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
}
