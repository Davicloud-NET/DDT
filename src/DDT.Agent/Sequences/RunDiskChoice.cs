// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Disks;

namespace DDT.Agent.Sequences;

// The disk a fresh run erases, checked against the run before anything on it is.
internal sealed class RunDiskChoice(IDiskPartitioner partitioner, IRawDisks rawDisks)
{
    public async Task<LocalDisk> ChooseAsync(AgentRun run, LocalDisk? confirmedDisk, RunInventory inventory, CancellationToken cancellationToken)
    {
        IReadOnlyList<LocalDisk> disks = await partitioner.ListDisksAsync(cancellationToken).ConfigureAwait(false);
        LocalDisk disk = Select(run.DiskNumber, confirmedDisk, disks);
        IReadOnlyList<SequenceStep> steps = inventory.Steps;
        IReadOnlyList<AgentRunImage> rawImages = inventory.RawImages;

        if (steps.Any(step => step.IsContainer))
        {
            RunDiskSpace.CheckTree(run, disk, rawImages.Count > 0);
        }
        else if (rawImages.Count == 0)
        {
            RunDiskSpace.CheckList(run, disk, inventory.Images, steps.OfType<PartitionStep>().FirstOrDefault());
        }
        else
        {
            RunDiskSpace.CheckRaw(run, disk, rawImages[0], steps.Any(step => step is WriteCloudInitSeedStep));
        }

        if (rawImages.Count > 0)
        {
            using IRawDisk raw = rawDisks.Open(disk);

            if (raw.SectorSize != GptLayout.SectorSize)
            {
                throw new DeploymentStepException($"Disk {disk.Number} {WriteRawImageStepRunner.FourKilobyteSectorsMessage}");
            }
        }

        return disk;
    }

    // Disk numbers can change when the machine starts again, so a disk chosen at the machine counts only while it is the
    // very disk confirmed in this process.
    private static LocalDisk Select(int? diskNumber, LocalDisk? confirmedDisk, IReadOnlyList<LocalDisk> disks)
    {
        if (diskNumber is { } number)
        {
            if (confirmedDisk is null || confirmedDisk.Number != number)
            {
                throw new DeploymentStepException(
                    $"Disk {number} was chosen before the agent started again, and disk numbers can change when a machine restarts, " +
                    "so nothing was erased. Choose the sequence and the disk again at this machine.");
            }

            LocalDisk disk = disks.FirstOrDefault(candidate => candidate.Number == number)
                ?? throw new DeploymentStepException(
                    $"Disk {number}, chosen at this machine, is not a disk DDT can install on now. Restart the machine from the network and choose again.");

            if (!disk.IsSameDiskAs(confirmedDisk))
            {
                throw new DeploymentStepException(
                    $"Disk {number} is no longer the disk chosen at this machine ({confirmedDisk.DisplayModel}, " +
                    $"{ByteSize.Format(confirmedDisk.SizeBytes)}), so nothing was erased. Choose the sequence and the disk again at this machine.");
            }

            return disk;
        }

        return disks.Count switch
        {
            0 => throw new DeploymentStepException(SequenceRunner.NoDiskMessage),
            1 => disks[0],
            _ => throw new DeploymentStepException(SequenceRunner.SeveralDisksMessage),
        };
    }
}
