// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Disks;

namespace DDT.Agent.LinuxPhase;

// A dry run's cloud-init seed. No kernel mounts it, so its files are read from the file that is the disk into a
// folder, which then stands in for the mounted seed. The file stays to be looked at, without the run's token.
public sealed class DryRunSeed(string folder, AgentLog log, TimeProvider timeProvider) : IMountedSeeds
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

        return 1;
    }

    // What the vendor-data script does when cloud-init runs it: the agent's settings, the run's state and its token
    // go from the seed to where the agent looks for them, and the token leaves the seed. False when the disk holds no
    // run that was handed over.
    public bool Install(string diskFile, LinuxAgentPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (!File.Exists(diskFile) || !Extract(diskFile))
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
        log.Information(
            "Dry run: no Linux is started. There cloud-init would run the agent's script from the seed, which installs the agent as " +
            $"{LinuxAgentFiles.UnitName}. Here its settings and the run go from the seed in {diskFile} to {paths.State}.");

        return true;
    }

    // Every file of the seed goes into the folder. The script wipes the token on the seed, so the seed in the file
    // is written again without it, and no byte of it stays there either.
    private bool Extract(string diskFile)
    {
        using FileStream disk = new(diskFile, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        byte[] head = new byte[(int)Math.Min(GptLayout.MaxHeadBytes, disk.Length)];
        disk.ReadExactly(head);

        try
        {
            if (GptLayout.Read(head).Partitions.FirstOrDefault(partition => partition.Name == CloudInitSeed.Label) is not { } partition
                || partition.Sectors * GptLayout.SectorSize != CloudInitSeed.SizeBytes)
            {
                return false;
            }

            byte[] volume = new byte[CloudInitSeed.SizeBytes];
            disk.Position = partition.FirstLba * GptLayout.SectorSize;
            disk.ReadExactly(volume);
            SeedVolume seed = SeedVolume.Read(volume);

            foreach (SeedFile file in seed.Files)
            {
                string target = Path.Combine(folder, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? folder);
                File.WriteAllBytes(target, file.Content);
            }

            WriteWithoutToken(disk, partition, seed);

            return true;
        }
        catch (Exception exception) when (exception is InvalidGptException or InvalidDataException or ArgumentException or IOException)
        {
            log.Warning($"Dry run: the seed in {diskFile} cannot be read ({exception.Message}).");

            return false;
        }
    }

    private void WriteWithoutToken(FileStream disk, GptPartition partition, SeedVolume seed)
    {
        if (!seed.Files.Any(IsToken))
        {
            return;
        }

        SeedFile[] kept = [.. seed.Files.Where(file => !IsToken(file))];
        disk.Position = partition.FirstLba * GptLayout.SectorSize;
        disk.Write(CloudInitSeed.Build(kept, seed.SerialNumber, timeProvider.GetUtcNow().UtcDateTime, partition.FirstLba));
        disk.Flush(flushToDisk: true);
    }

    private static bool IsToken(SeedFile file) => SeedVolume.SamePath(file.Path, LinuxAgentFiles.SeedToken);

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to) ?? ".");
        File.Copy(from, to, overwrite: true);
    }
}
