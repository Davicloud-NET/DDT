// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Disks;

namespace DDT.Agent.LinuxPhase;

// A dry run's cloud-init seed. No kernel mounts it, so its files are read from the file that is the disk into a
// folder, which then stands in for the mounted seed. What the agent removes there leaves the seed in the file too.
public sealed class DryRunSeed(string folder, string diskFile, AgentLog log, TimeProvider timeProvider) : IMountedSeeds
{
    public int Each(Action<string> visit)
    {
        ArgumentNullException.ThrowIfNull(visit);

        if (!Directory.Exists(folder))
        {
            return 0;
        }

        log.Information($"Dry run: no seed is mounted. {folder} stands in for the CIDATA volume.");
        visit(folder);
        Rewrite(file => File.Exists(Path.Combine(folder, file.Path)));

        return 1;
    }

    // What the vendor-data script does when cloud-init runs it: the agent's settings, the run's state and its token
    // go from the seed to where the agent looks for them, and the token leaves the seed. False when the disk holds no
    // run that was handed over.
    public bool Install(LinuxAgentPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (!Extract())
        {
            return false;
        }

        string ours = Path.Combine(folder, LinuxAgentFiles.SeedFolder);
        string token = Path.Combine(ours, Path.GetFileName(LinuxAgentFiles.SeedToken));
        RunFiles files = RunFiles.In(paths.State, log);

        if (!File.Exists(token))
        {
            return false;
        }

        Copy(Path.Combine(ours, Path.GetFileName(LinuxAgentFiles.SeedConfiguration)), paths.Configuration);
        Copy(Path.Combine(ours, Path.GetFileName(LinuxAgentFiles.SeedState)), files.StatePath);
        Copy(token, files.TokenPath);
        Leftovers.Wipe(token, log);
        Rewrite(file => !SeedVolume.SamePath(file.Path, LinuxAgentFiles.SeedToken));
        log.Information(
            "Dry run: no Linux is started. There cloud-init would run the agent's script from the seed, which installs the agent as " +
            $"{LinuxAgentFiles.UnitName}. Here its settings and the run go from the seed in {diskFile} to {paths.State}.");

        return true;
    }

    // Every file of the seed goes into the folder.
    private bool Extract()
    {
        if (Read() is not { } seed)
        {
            return false;
        }

        foreach (SeedFile file in seed.Volume.Files)
        {
            string target = Path.Combine(folder, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? folder);
            File.WriteAllBytes(target, file.Content);
        }

        return true;
    }

    // Writes the seed in the file again with the files to keep, as a kernel writes out what changed on a mounted
    // seed. The whole volume is written, so no byte of a file that went stays there either.
    private void Rewrite(Func<SeedFile, bool> keeps)
    {
        if (Read() is not { } seed || seed.Volume.Files.All(keeps))
        {
            return;
        }

        using FileStream disk = new(diskFile, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        disk.Position = seed.Partition.FirstLba * GptLayout.SectorSize;
        disk.Write(CloudInitSeed.Build([.. seed.Volume.Files.Where(keeps)], seed.Volume.SerialNumber, timeProvider.GetUtcNow().UtcDateTime, seed.Partition.FirstLba));
        disk.Flush(flushToDisk: true);
    }

    // The seed as the file that is the disk holds it. Null, after a warning when it cannot be read, where there is none.
    private (GptPartition Partition, SeedVolume Volume)? Read()
    {
        if (!File.Exists(diskFile))
        {
            return null;
        }

        try
        {
            using FileStream disk = new(diskFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] head = new byte[(int)Math.Min(GptLayout.MaxHeadBytes, disk.Length)];
            disk.ReadExactly(head);

            if (GptLayout.Read(head).Partitions.FirstOrDefault(partition => partition.Name == CloudInitSeed.Label) is not { } partition
                || partition.Sectors * GptLayout.SectorSize != CloudInitSeed.SizeBytes)
            {
                return null;
            }

            byte[] volume = new byte[CloudInitSeed.SizeBytes];
            disk.Position = partition.FirstLba * GptLayout.SectorSize;
            disk.ReadExactly(volume);

            return (partition, SeedVolume.Read(volume));
        }
        catch (Exception exception) when (exception is InvalidGptException or InvalidDataException or ArgumentException or IOException)
        {
            log.Warning($"Dry run: the seed in {diskFile} cannot be read ({exception.Message}).");

            return null;
        }
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to) ?? ".");
        File.Copy(from, to, overwrite: true);
    }
}
