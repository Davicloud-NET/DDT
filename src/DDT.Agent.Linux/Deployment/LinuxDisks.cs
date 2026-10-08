// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Disks;

namespace DDT.Agent.Deployment;

// The machine's disks as /sys/block lists them, with the rules Windows PE has for which of them DDT may install on.
// realPath says where an entry of /sys/block leads, which is how the kernel shows what a disk is attached to.
public sealed class LinuxDisks(string root, IRawDisks rawDisks, AgentLog log, Func<string, string>? realPath = null) : IDisks
{
    private const int Mebibyte = 1024 * 1024;

    private readonly Func<string, string> _realPath = realPath ?? RealPath;

    private string Block => Path.Combine(root, "sys", "block");

    // A disk's number is its place among the disks the kernel lists, so it can change when the machine starts again.
    public Task<IReadOnlyList<LocalDisk>> ListDisksAsync(CancellationToken cancellationToken)
    {
        List<LocalDisk> disks = [];
        int number = 0;

        foreach (string name in Names())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Probe(name, number++) is { } disk)
            {
                disks.Add(disk);
            }
        }

        return Task.FromResult<IReadOnlyList<LocalDisk>>(disks);
    }

    // Zeroes the first and the last mebibyte of the disk and of each partition it had. That takes the partition tables
    // and what file systems, RAID sets, volume groups and encrypted volumes are recognised by.
    public Task<IReadOnlyList<Guid>> CleanAsync(LocalDisk disk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(disk);

        using IRawDisk raw = rawDisks.Open(disk);
        IReadOnlyList<GptPartition> partitions = OldPartitions(raw);
        List<Guid> erased = [.. partitions.Where(partition => partition.Type == GptPartitionTypes.EfiSystem).Select(partition => partition.Id)];

        log.Information(
            $"Erasing the partition table of disk {disk.Number}, {disk.Device}: zeroing the first and the last mebibyte of the disk " +
            $"and of {LocalDisk.Partitions(partitions.Count)}.");

        foreach (GptPartition partition in partitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ZeroEnds(raw, partition.FirstLba * raw.SectorSize, partition.Sectors * raw.SectorSize);
        }

        ZeroEnds(raw, 0, raw.Length);
        raw.Flush();

        try
        {
            raw.UpdateProperties();
        }
        catch (DeploymentStepException exception)
        {
            log.Warning($"{exception.Message} The disk is erased all the same.");
        }

        return Task.FromResult<IReadOnlyList<Guid>>(erased);
    }

    private List<string> Names()
    {
        try
        {
            return [.. Directory.EnumerateDirectories(Block).Select(Path.GetFileName).OfType<string>().Where(DiskNames.IsDisk).Order(DiskNames.Order)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"The kernel's list of disks cannot be read ({exception.Message}).");

            return [];
        }
    }

    private LocalDisk? Probe(string name, int number)
    {
        string block = Path.Combine(Block, name);

        if (SysfsText.ReadNumber(Path.Combine(block, "size")) is not { } units)
        {
            log.Warning($"The size of disk {number}, {name}, cannot be read. It is left out.");

            return null;
        }

        bool removable = SysfsText.ReadNumber(Path.Combine(block, "removable")) is > 0;
        StorageBusType bus = LinuxBusType.Of(name, _realPath(block), SysfsText.Read(Path.Combine(block, "device", "type")));
        long size = units * LinuxRawDisks.SizeUnit;
        string? reason = DiskEligibility.ExclusionReason(removable, bus, size);
        LocalDisk disk = new(number, Model(name, block), size, bus, PartitionCount(block)) { Device = Path.Combine(root, "dev", name) };

        log.Information(
            $"{disk.Describe()}, removable {(removable ? "yes" : "no")}: " +
            (reason is null ? "DDT can install on it." : $"left out because {reason}."));

        return reason is null ? disk : null;
    }

    // SCSI and SATA disks have a vendor and a model, NVMe disks a model, cards a name. Other disks have none of them:
    // what a virtio disk calls its vendor is a number.
    private static string? Model(string name, string block)
    {
        string device = Path.Combine(block, "device");
        string? vendor = name.StartsWith("sd", StringComparison.Ordinal) ? SysfsText.Read(Path.Combine(device, "vendor")) : null;
        string?[] parts = [vendor, SysfsText.Read(Path.Combine(device, "model")) ?? SysfsText.Read(Path.Combine(device, "name"))];
        string model = string.Join(' ', parts.Where(part => part is not null));

        return model.Length == 0 ? null : model;
    }

    // The kernel shows each partition as a folder of the disk that holds a file named partition.
    private static int PartitionCount(string block)
    {
        try
        {
            return Directory.EnumerateDirectories(block).Count(entry => File.Exists(Path.Combine(entry, "partition")));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    // A disk without a partition table DDT can read has no partitions to erase.
    private static IReadOnlyList<GptPartition> OldPartitions(IRawDisk raw)
    {
        byte[] head = new byte[(int)Math.Min(GptLayout.MaxHeadBytes, raw.Length)];
        raw.Read(0, head);

        try
        {
            return raw.SectorSize == GptLayout.SectorSize ? GptLayout.Read(head).Partitions : [];
        }
        catch (Exception exception) when (exception is InvalidGptException or ArgumentException)
        {
            return [];
        }
    }

    // An old table may name sectors the disk does not have.
    private static void ZeroEnds(IRawDisk raw, long start, long length)
    {
        long end = Math.Min(start + length, raw.Length);

        if (start < 0 || start >= end)
        {
            return;
        }

        byte[] zeroes = new byte[(int)Math.Min(Mebibyte, end - start)];
        raw.Write(start, zeroes);
        raw.Write(end - zeroes.Length, zeroes);
    }

    private static string RealPath(string path) => new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
}
