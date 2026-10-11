// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Core.CloudInit;
using DDT.Core.Disks;

namespace DDT.Agent.Sequences;

// Reads a run's cloud-init seed from its disk for the hand-over. A script after the seed step has the disk, so the
// seed has to be where the step wrote it still, and a volume that can be built again with every file it holds now.
internal static class SeedOnDisk
{
    public static string GoneMessage(WrittenSeed seed, LocalDisk disk) =>
        $"The cloud-init seed is no longer partition {seed.PartitionNumber} of disk {disk.Number}, where this run wrote it, so the " +
        "agent cannot be put on it. Nothing was written to the disk.";

    public static string OtherVolumeMessage(WrittenSeed seed, LocalDisk disk, string why) =>
        $"Partition {seed.PartitionNumber} of disk {disk.Number} is no longer the cloud-init seed this run wrote, a FAT volume labelled " +
        $"{CloudInitSeed.Label}, so the agent cannot be put on it: {why} A step after the seed step may change the seed's files, " +
        "but not its file system. Nothing was written to the disk.";

    public static string NameTakenMessage(string path) =>
        $"The cloud-init seed holds {path}, which a step after the seed step put there. The run goes on in the installed Linux " +
        $"through a {CloudInitSeed.VendorData} file and a {LinuxAgentFiles.SeedFolder} folder of DDT's own on the seed, so the " +
        "sequence cannot bring its own. Nothing was written to the disk.";

    public static string UnreadMessage(LocalDisk disk, string why) =>
        $"The cloud-init seed of disk {disk.Number} could not be read to put the agent on it ({why}). Nothing was written to the disk.";

    public static SeedVolume Read(IRawDisk raw, WrittenSeed seed, LocalDisk disk)
    {
        byte[] head = new byte[(int)Math.Min(RawDiskWriter.HeadBytes, raw.Length)];
        byte[] volume = new byte[CloudInitSeed.SizeBytes];
        Read(raw, 0, head, disk);
        RequirePartition(head, seed, disk);
        Read(raw, seed.FirstSector * GptLayout.SectorSize, volume, disk);
        SeedVolume found = Parse(volume, seed, disk);

        return found.Files.FirstOrDefault(file => LinuxAgentFiles.IsForSeed(file.Path)) is { } taken
            ? throw new DeploymentStepException(NameTakenMessage(taken.Path))
            : found;
    }

    private static void Read(IRawDisk raw, long offset, byte[] buffer, LocalDisk disk)
    {
        try
        {
            raw.Read(offset, buffer);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            throw new DeploymentStepException(UnreadMessage(disk, exception.Message), exception);
        }
    }

    // The table may have other partitions by now. The seed's own entry has to be the one its step made.
    private static void RequirePartition(byte[] head, WrittenSeed seed, LocalDisk disk)
    {
        try
        {
            if (GptLayout.Read(head).Partitions.Any(partition =>
                partition.Number == seed.PartitionNumber
                && partition.FirstLba == seed.FirstSector
                && partition.Sectors == CloudInitSeed.SizeBytes / GptLayout.SectorSize))
            {
                return;
            }
        }
        catch (Exception exception) when (exception is InvalidGptException or ArgumentException)
        {
            throw new DeploymentStepException(GoneMessage(seed, disk), exception);
        }

        throw new DeploymentStepException(GoneMessage(seed, disk));
    }

    // The reader's own sentence says what is wrong with the volume. A folder it cannot follow is an IOException.
    private static SeedVolume Parse(byte[] volume, WrittenSeed seed, LocalDisk disk)
    {
        try
        {
            return SeedVolume.Read(volume);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            throw new DeploymentStepException(OtherVolumeMessage(seed, disk, exception.Message), exception);
        }
    }
}
