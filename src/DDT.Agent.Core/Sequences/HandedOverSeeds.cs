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
// is read from its disk without mounting it. A disk that cannot be read holds none, which is said once. A seed that
// cannot be read may hold a run, so it counts as one that names none.
public sealed class HandedOverSeeds(IRawDisks disks, AgentLog log, TimeProvider timeProvider)
{
    // A token is a line of text. A file this large is none.
    private const int MaxTokenBytes = 16 * 1024;

    private const int MaxStateBytes = 4 * 1024 * 1024;

    // A hand-over's disk has one seed. Each partition that looks like one is 64 MiB to read before the agent registers.
    public const int MaxSeedsPerDisk = 4;

    public static string UnreadMessage(LocalDisk disk, string why)
    {
        ArgumentNullException.ThrowIfNull(disk);

        return $"Disk {disk.Number} could not be looked at for a run that was handed over to the installed Linux ({why}). The agent " +
            "goes on as if it held none.";
    }

    public static string TooManySeedsMessage(LocalDisk disk, int found)
    {
        ArgumentNullException.ThrowIfNull(disk);

        return $"Disk {disk.Number} has {found} partitions with a cloud-init seed's name and size. The agent looked at the first " +
            $"{MaxSeedsPerDisk} for a run that was handed over to the installed Linux, and not at the others.";
    }

    public static string SeedUnreadMessage(LocalDisk disk, string why)
    {
        ArgumentNullException.ThrowIfNull(disk);

        return $"The cloud-init seed of disk {disk.Number} cannot be read ({why}). A run that was handed over to the installed Linux " +
            "may be on it, so the agent takes the seed for one that holds a run it cannot name.";
    }

    public static string TokenUnreadMessage(LocalDisk disk, string why)
    {
        ArgumentNullException.ThrowIfNull(disk);

        return $"The file {LinuxAgentFiles.SeedToken} on the cloud-init seed of disk {disk.Number} cannot be read ({why}), so the agent " +
            "has no token of a run from it.";
    }

    public static string NoTokenMessage(LocalDisk disk, long bytes)
    {
        ArgumentNullException.ThrowIfNull(disk);

        return $"The file {LinuxAgentFiles.SeedToken} on the cloud-init seed of disk {disk.Number} holds {bytes} bytes that are no token of a run, " +
            "so the agent does not read it as one.";
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

    // The seeds that hold a run, the one whose run was assigned last first. Before them comes a seed without a token
    // that names no run: its run may be the last one, and only the server knows.
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

        return [.. found.OrderByDescending(seed => seed is { IsTaken: true, RunId: null }).ThenByDescending(seed => seed.RunTime)];
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

        GptPartition[] seeds = [.. layout.Partitions.Where(IsSeed)];

        if (seeds.Length > MaxSeedsPerDisk)
        {
            log.Warning(TooManySeedsMessage(disk, seeds.Length));
        }

        return [.. seeds.Take(MaxSeedsPerDisk).Select(partition => Read(raw, disk, partition, system)).OfType<HandedOverSeed>()];
    }

    // The seed step names its partition after the label and gives it this size.
    private static bool IsSeed(GptPartition partition) =>
        partition.Name == CloudInitSeed.Label && partition.Sectors * GptLayout.SectorSize == CloudInitSeed.SizeBytes;

    // Null for a seed that holds neither the state nor the token of a run. One whose file system cannot be read, or
    // whose sectors the disk does not give, is not taken for empty: the registration that follows would end a run
    // that goes on in the installed Linux.
    private HandedOverSeed? Read(IRawDisk raw, LocalDisk disk, GptPartition partition, EspPartition? system)
    {
        byte[] bytes;

        try
        {
            bytes = ReadPartition(raw, partition);
        }
        catch (Exception exception) when (exception is DeploymentStepException or IOException)
        {
            return Unread(disk, partition, system, exception);
        }

        try
        {
            FatVolume volume = FatVolume.Open(new MemoryStream(bytes, writable: false), 0, bytes.Length);

            if (!string.Equals(volume.Label, CloudInitSeed.Label, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            FatEntry? state = volume.Find(LinuxAgentFiles.SeedState);
            string? token = volume.Find(LinuxAgentFiles.SeedToken) is { IsDirectory: false } entry ? Token(volume, entry, disk) : null;

            return state is null && token is null
                ? null
                : new HandedOverSeed(disk, partition) { Token = token, RunId = RunId(volume, state), SystemPartition = system };
        }
        catch (InvalidDataException exception)
        {
            return Unread(disk, partition, system, exception);
        }
    }

    // The disk's table names the seed, so it is there, and only what it holds is not known.
    private HandedOverSeed Unread(LocalDisk disk, GptPartition partition, EspPartition? system, Exception exception)
    {
        log.Warning(SeedUnreadMessage(disk, LogText.OneLine(exception)));

        return new HandedOverSeed(disk, partition) { SystemPartition = system, IsUnread = true };
    }

    private static byte[] ReadPartition(IRawDisk raw, GptPartition partition)
    {
        byte[] bytes = new byte[CloudInitSeed.SizeBytes];
        raw.Read(partition.FirstLba * GptLayout.SectorSize, bytes);

        return bytes;
    }

    // The script overwrites the token with zeros before it deletes it, and power may be lost between the two. A file
    // that holds anything but a token, or cannot be read, is said and counts as none: sent as a token, other bytes
    // would end the run on the server.
    private string? Token(FatVolume volume, FatEntry entry, LocalDisk disk)
    {
        if (entry.Size <= MaxTokenBytes)
        {
            if (TokenBytes(volume, entry, disk) is not { } content)
            {
                return null;
            }

            ReadOnlySpan<byte> line = RunFiles.Trimmed(content, zerosToo: true);

            if (line.Length == 0)
            {
                return null;
            }

            if (RunFiles.IsToken(line))
            {
                return Encoding.ASCII.GetString(line);
            }
        }

        log.Warning(NoTokenMessage(disk, entry.Size));

        return null;
    }

    // Null, after a warning, for a file whose clusters cannot be read.
    private byte[]? TokenBytes(FatVolume volume, FatEntry entry, LocalDisk disk)
    {
        try
        {
            return volume.ReadFile(entry, MaxTokenBytes);
        }
        catch (InvalidDataException exception)
        {
            log.Warning(TokenUnreadMessage(disk, LogText.OneLine(exception)));

            return null;
        }
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
