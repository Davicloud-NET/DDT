// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Deployment;
using DDT.Core.CloudInit;

namespace DDT.Agent.LinuxPhase;

// Finds the FAT volumes labelled CIDATA and mounts each with the kernel's own call. An image may bring a seed of its
// own beside the one the run was handed over on, so every one is visited.
public sealed class LinuxSeeds(IMounts mounts, LinuxAgentPaths paths, AgentLog log) : IMountedSeeds
{
    public const string FileSystem = "vfat";

    // Root's alone, as the script mounts it.
    public const string Options = "umask=077";

    // Where a FAT16 and a FAT32 boot sector keep the volume's label.
    private static readonly int[] s_labelOffsets = [43, 71];

    public int Each(Action<string> visit)
    {
        ArgumentNullException.ThrowIfNull(visit);

        string target = paths.SeedMount;
        int mounted = 0;

        foreach (string device in Devices())
        {
            Directory.CreateDirectory(target);

            if (mounts.Mount(device, target, FileSystem, Options) != 0)
            {
                continue;
            }

            mounted++;

            try
            {
                visit(target);
            }
            finally
            {
                Unmount(target, device);
            }
        }

        RemovePlace(target);

        return mounted;
    }

    // The agent's place for a seed goes once nothing is mounted there. The folder above it is not a seed's to remove.
    private static void RemovePlace(string target)
    {
        try
        {
            Directory.Delete(target, recursive: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not there, or still mounted: /run is empty again after the next start
        }
    }

    // The device the system's label link names, then every partition whose boot sector carries the label.
    public IReadOnlyList<string> Devices()
    {
        List<string> devices = [];
        string link = paths.At($"/dev/disk/by-label/{CloudInitSeed.Label}");

        if (File.Exists(link))
        {
            devices.Add(File.ResolveLinkTarget(link, returnFinalTarget: true)?.FullName ?? link);
        }

        try
        {
            devices.AddRange(Directory.EnumerateDirectories(paths.At("/sys/class/block"))
                .Where(block => File.Exists(Path.Combine(block, "partition")))
                .Select(block => paths.At("/dev/" + Path.GetFileName(block)))
                .Where(IsSeed)
                .Order(StringComparer.Ordinal));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Without the kernel's list the label link is all there is
        }

        return [.. devices.Distinct(StringComparer.Ordinal)];
    }

    private static bool IsSeed(string device)
    {
        try
        {
            using FileStream stream = new(device, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] sector = new byte[512];

            return stream.ReadAtLeast(sector, sector.Length, throwOnEndOfStream: false) == sector.Length
                && s_labelOffsets.Any(offset => Encoding.ASCII.GetString(sector, offset, 11).TrimEnd() == CloudInitSeed.Label);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // The plain call writes what is left to the disk before it returns. A file still open elsewhere gets the other.
    private void Unmount(string target, string device)
    {
        if (mounts.Unmount(target, detach: false) == 0)
        {
            return;
        }

        int error = mounts.Unmount(target, detach: true);

        if (error != 0)
        {
            log.Warning($"The cloud-init seed {device} could not be unmounted from {target} (error {error}).");
        }
    }
}
