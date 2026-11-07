// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Whether a disk can hold a run, checked before anything on it is erased. Each check throws with what the run needs.
internal static class RunDiskSpace
{
    // 2 GB more keeps the downloads and the applied image from filling the disk to the last byte.
    private const long SpareBytes = 2048L * 1024 * 1024;
    private const long Megabyte = 1024L * 1024;

    // The partitions, each image's download and installed files, each package twice to unpack it, and some to spare.
    public static void CheckList(AgentRun run, LocalDisk disk, IReadOnlyList<AgentRunImage> images, PartitionStep? partition)
    {
        int system = partition?.SystemPartitionMegabytes ?? DiskpartScript.SystemPartitionMegabytes;
        int recovery = partition?.RecoveryPartitionMegabytes ?? DiskpartScript.RecoveryPartitionMegabytes;
        long partitions = (system + DiskpartScript.ReservedPartitionMegabytes + recovery) * Megabyte;
        long downloads = images.Sum(image => image.SizeBytes);
        long installed = images.Sum(image => image.InstalledBytes);
        long packages = 2 * run.Packages.Sum(package => package.SizeBytes);
        long required = partitions + downloads + installed + packages + SpareBytes;

        if (disk.SizeBytes >= required)
        {
            return;
        }

        List<string> parts = [$"{ByteSize.Format(partitions)} for the boot and recovery partitions"];

        if (images.Count > 0)
        {
            parts.Add($"{ByteSize.Format(downloads)} for the image downloads");
            parts.Add($"{ByteSize.Format(installed)} for the installed files");
        }

        if (packages > 0)
        {
            parts.Add($"{ByteSize.Format(packages)} for the packages and their contents");
        }

        throw new DeploymentStepException(
            $"Disk {disk.Number} holds {ByteSize.Format(disk.SizeBytes)}, but {run.SequenceName} needs {ByteSize.Format(required)}: " +
            $"{string.Join(", ", parts)} and {ByteSize.Format(SpareBytes)} to spare. Run it on a larger disk.");
    }

    // Checks the path through the tree that needs the most, as SequenceSizes works it out, with a list's rules for each
    // step. A raw disk image is written as it downloads, so it only needs its disk and nothing to spare. A package the
    // server sent for no step in the tree counts on every path.
    public static void CheckTree(AgentRun run, LocalDisk disk, bool raw)
    {
        HashSet<Guid> nodes = [.. SequenceTree.Nodes(run.Sequence).Select(node => node.Id)];

        long Packages(Func<AgentRunPackage, bool> which) => raw ? 0 : 2 * run.Packages.Where(which).Sum(package => package.SizeBytes);

        long FileBytes(SequenceStep step) => step switch
        {
            ApplyImageStep apply when !raw => run.Images.FirstOrDefault(image => image.ImageId == apply.ImageId) is { } image
                ? image.SizeBytes + image.InstalledBytes
                : 0,
            WriteRawImageStep write => WriteRawImageStepRunner.ImageOf(run, write).InstalledBytes,
            _ => Packages(package => package.StepId == step.Id),
        };

        long required = SequenceSizes.RequiredBytes(run.Sequence, FileBytes) + Packages(package => !nodes.Contains(package.StepId)) + (raw ? 0 : SpareBytes);

        if (disk.SizeBytes >= required)
        {
            return;
        }

        throw new DeploymentStepException(
            $"Disk {disk.Number} holds {ByteSize.Format(disk.SizeBytes)}, but {run.SequenceName} needs {ByteSize.Format(required)} on the " +
            $"path through it that needs the most{(raw ? "" : $", {ByteSize.Format(SpareBytes)} to spare included")}. Run it on a larger disk.");
    }

    // A raw disk image is written as it downloads, so the disk only needs room for the disk the image holds and the
    // seed.
    public static void CheckRaw(AgentRun run, LocalDisk disk, AgentRunImage image, bool seed)
    {
        long required = image.InstalledBytes + (seed ? CloudInitSeed.DiskBytes : 0);

        if (disk.SizeBytes >= required)
        {
            return;
        }

        throw new DeploymentStepException(
            $"Disk {disk.Number} holds {ByteSize.Format(disk.SizeBytes)}, but {run.SequenceName} needs {ByteSize.Format(required)}: " +
            $"{ByteSize.Format(image.InstalledBytes)} for the disk image" +
            (seed ? $" and {ByteSize.Format(CloudInitSeed.DiskBytes)} for the cloud-init seed" : "") +
            ". Run it on a larger disk.");
    }
}
