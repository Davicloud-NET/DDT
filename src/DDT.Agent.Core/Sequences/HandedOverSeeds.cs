// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using System.Text.Json;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Core.CloudInit;
using DDT.Core.Disks;

namespace DDT.Agent.Sequences;

// Looks at disks for a run that a hand-over put on a cloud-init seed, and takes such a run off its seed again. A seed
// is read from its disk without mounting it. A disk that cannot be read holds none, which is said once.
public sealed class HandedOverSeeds(IRawDisks disks, AgentLog log, TimeProvider timeProvider)
{
    // A token is a line of text. A file this large is none.
    private const int MaxTokenBytes = 16 * 1024;

    private const int MaxStateBytes = 4 * 1024 * 1024;

    public static string UnreadMessage(LocalDisk disk, string why)
    {
        ArgumentNullException.ThrowIfNull(disk);

        return $"Disk {disk.Number} could not be looked at for a run that was handed over to the installed Linux ({why}). The agent " +
            "goes on as if it held none.";
    }

    public static string RemovedMessage(LocalDisk disk)
    {
        ArgumentNullException.ThrowIfNull(disk);

        return $"What the hand-over put on the cloud-init seed of disk {disk.Number} is removed again: the agent, its script and the run's files.";
    }

    public static string NotRemovedMessage(LocalDisk disk, string why)
    {
        ArgumentNullException.ThrowIfNull(disk);

        return $"What the hand-over put on the cloud-init seed of disk {disk.Number} could not be removed ({why}). A token there is " +
            "one the server takes only while it runs that run. A later start from the network finds the seed again.";
    }

    // The seeds that hold a run, the one whose run was assigned last first.
    public IReadOnlyList<HandedOverSeed> Find(IEnumerable<LocalDisk> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        List<HandedOverSeed> found = [];

        foreach (LocalDisk disk in candidates)
        {
            try
            {
                found.AddRange(On(disk));
            }
            catch (Exception exception) when (IsUnread(exception))
            {
                log.Warning(UnreadMessage(disk, LogText.OneLine(exception)));
            }
        }

        return [.. found.OrderByDescending(seed => seed.RunTime)];
    }

    // Writes the seed again without what the hand-over added, so no byte of the token stays in its partition. Never
    // throws. False, after a warning, when the seed stays as it is. What went well is the caller's to say.
    public bool TakeOff(HandedOverSeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);

        try
        {
            using IRawDisk raw = disks.Open(seed.Disk);
            SeedVolume volume = SeedVolume.Read(ReadPartition(raw, seed.Partition));
            SeedFile[] kept = [.. volume.Files.Where(file => !IsHandOvers(file))];
            raw.Write(seed.Partition.FirstLba * GptLayout.SectorSize, CloudInitSeed.Build(kept, volume.SerialNumber, timeProvider.GetUtcNow().UtcDateTime, seed.Partition.FirstLba));
            raw.Flush();

            return true;
        }
        catch (Exception exception)
        {
            // Whatever fails here is said and not thrown on: the agent still has to go on as after any start.
            log.Warning(NotRemovedMessage(seed.Disk, LogText.OneLine(exception)));

            return false;
        }
    }

    // A vendor-data that is not DDT's script is the administrator's and stays.
    private static bool IsHandOvers(SeedFile file) =>
        LinuxAgentFiles.IsForSeed(file.Path) && (!SeedVolume.SamePath(file.Path, CloudInitSeed.VendorData) || VendorDataScript.IsOurs(file.Content));

    private static bool IsUnread(Exception exception) =>
        exception is DeploymentStepException or IOException or UnauthorizedAccessException or InvalidGptException or InvalidDataException or ArgumentException;

    // A disk without a table for 512-byte sectors is none DDT wrote an image to, so it is not even said.
    private List<HandedOverSeed> On(LocalDisk disk)
    {
        using IRawDisk raw = disks.Open(disk);
        byte[] head = new byte[(int)Math.Min(GptLayout.MaxHeadBytes, raw.Length)];
        raw.Read(0, head);

        if (GptLayout.SectorSizeOf(head) != GptLayout.SectorSize)
        {
            return [];
        }

        GptLayout layout = GptLayout.Read(head);
        EspPartition? system = layout.Partitions.FirstOrDefault(partition => partition.Type == GptPartitionTypes.EfiSystem) is { } esp
            ? new EspPartition((uint)esp.Number, (ulong)esp.FirstLba, (ulong)esp.Sectors, esp.Id)
            : null;

        return [.. layout.Partitions.Where(IsSeed).Select(partition => Read(raw, disk, partition, system)).OfType<HandedOverSeed>()];
    }

    // The seed step names its partition after the label and gives it this size.
    private static bool IsSeed(GptPartition partition) =>
        partition.Name == CloudInitSeed.Label && partition.Sectors * GptLayout.SectorSize == CloudInitSeed.SizeBytes;

    // Null for a seed that holds neither the state nor the token of a run.
    private static HandedOverSeed? Read(IRawDisk raw, LocalDisk disk, GptPartition partition, EspPartition? system)
    {
        byte[] bytes = ReadPartition(raw, partition);
        FatVolume volume = FatVolume.Open(new MemoryStream(bytes, writable: false), 0, bytes.Length);

        if (!string.Equals(volume.Label, CloudInitSeed.Label, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        FatEntry? state = volume.Find(LinuxAgentFiles.SeedState);
        string? token = volume.Find(LinuxAgentFiles.SeedToken) is { IsDirectory: false } entry ? Token(volume.ReadFile(entry, MaxTokenBytes)) : null;

        return state is null && token is null
            ? null
            : new HandedOverSeed(disk, partition) { Token = token, RunId = RunId(volume, state), SystemPartition = system };
    }

    private static byte[] ReadPartition(IRawDisk raw, GptPartition partition)
    {
        byte[] bytes = new byte[CloudInitSeed.SizeBytes];
        raw.Read(partition.FirstLba * GptLayout.SectorSize, bytes);

        return bytes;
    }

    // The script overwrites the token with zeros before it deletes it, and power may be lost between the two.
    private static string? Token(byte[] content)
    {
        string token = Encoding.ASCII.GetString(content).Trim('\0', ' ', '\t', '\r', '\n');

        return token.Length == 0 ? null : token;
    }

    // Only for the log and to tell an older run from a newer one, so a state that cannot be read names none.
    private static Guid? RunId(FatVolume volume, FatEntry? state)
    {
        try
        {
            return state is { IsDirectory: false }
                ? JsonSerializer.Deserialize(volume.ReadFile(state, MaxStateBytes), AgentJsonContext.Default.SequenceState)?.RunId
                : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }
}
