// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.Deployment;

// Finds disks with IOCTLs and partitions one with diskpart. workDirectory holds the script (X:\DDT in Windows PE).
public sealed class DiskpartPartitioner(IToolRunner tools, AgentLog log, TimeProvider timeProvider, string workDirectory) : IDiskPartitioner
{
    // Disk numbers can have gaps, so a missing number does not end the search.
    private const int MaxDisks = 32;

    private static readonly TimeSpan s_volumeTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_volumePollInterval = TimeSpan.FromMilliseconds(500);

    private readonly DiskLayouts _layouts = new(log);

    public Task<IReadOnlyList<LocalDisk>> ListDisksAsync(CancellationToken cancellationToken)
    {
        DiskProbe probe = new(log, _layouts);
        List<LocalDisk> disks = [];

        for (int number = 0; number < MaxDisks; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (probe.Probe(number) is { } disk)
            {
                disks.Add(disk);
            }
        }

        return Task.FromResult<IReadOnlyList<LocalDisk>>(disks);
    }

    public async Task<TargetVolumes> PartitionAsync(
        LocalDisk disk,
        int systemPartitionMegabytes,
        int recoveryPartitionMegabytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(disk);

        PartitionLetters letters = DriveLetters.Choose(DiskNativeMethods.GetLogicalDrives());
        string script = DiskpartScript.Build(disk.Number, letters, systemPartitionMegabytes, recoveryPartitionMegabytes);
        string path = await WriteScriptAsync("partition.txt", script, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<Guid> erased = _layouts.ReadSystemPartitionIds(disk.Number);

        log.Information($"Partitioning disk {disk.Number} with this diskpart script:");
        LogScript(script);
        await RunScriptAsync(path, cancellationToken).ConfigureAwait(false);

        TargetVolumes volumes = new($"{letters.System}:\\", $"{letters.Windows}:\\", $"{letters.Recovery}:\\", erased);

        foreach (string root in new[] { volumes.System, volumes.Windows, volumes.Recovery })
        {
            await WaitForVolumeAsync(root, cancellationToken).ConfigureAwait(false);
        }

        return volumes with
        {
            SystemPartitionId = PartitionReader.ReadId(volumes.System),
            WindowsPartitionId = PartitionReader.ReadId(volumes.Windows),
            RecoveryPartitionId = PartitionReader.ReadId(volumes.Recovery),
        };
    }

    public async Task<IReadOnlyList<Guid>> CleanAsync(LocalDisk disk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(disk);

        string path = await WriteScriptAsync("clean.txt", DiskpartScript.Clean(disk.Number), cancellationToken).ConfigureAwait(false);
        IReadOnlyList<Guid> erased = _layouts.ReadSystemPartitionIds(disk.Number);

        log.Information($"Erasing the partition table of disk {disk.Number} with diskpart clean.");
        await RunScriptAsync(path, cancellationToken).ConfigureAwait(false);

        return erased;
    }

    public async Task<TargetVolumes> FindAsync(RunDiskIds ids, string windowsRoot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (PartitionReader.ReadId(windowsRoot) != ids.Windows)
        {
            throw new DeploymentStepException(
                $"{windowsRoot} holds a run's state but is not the Windows partition that run made, so the run cannot go on.");
        }

        int number = DiskLayouts.ReadDiskNumber(windowsRoot);
        byte[] layout = ReadRunLayout(number);
        uint system = PartitionNumber(layout, ids.System, "system", number);
        uint recovery = PartitionNumber(layout, ids.Recovery, "recovery", number);

        (char systemLetter, _, char recoveryLetter) = DriveLetters.Choose(DiskNativeMethods.GetLogicalDrives());
        string script = DiskpartScript.AssignLetters(number, system, systemLetter, recovery, recoveryLetter);
        string path = await WriteScriptAsync("find.txt", script, cancellationToken).ConfigureAwait(false);

        log.Information($"The run's Windows partition is {windowsRoot} on disk {number}. Its system and recovery partitions get letters again:");
        LogScript(script);
        await RunScriptAsync(path, cancellationToken).ConfigureAwait(false);

        TargetVolumes volumes = new($"{systemLetter}:\\", windowsRoot, $"{recoveryLetter}:\\", ids.ErasedSystemPartitionIds)
        {
            SystemPartitionId = ids.System,
            WindowsPartitionId = ids.Windows,
            RecoveryPartitionId = ids.Recovery,
        };

        await WaitForVolumeAsync(volumes.System, cancellationToken).ConfigureAwait(false);
        await WaitForVolumeAsync(volumes.Recovery, cancellationToken).ConfigureAwait(false);

        // The run's end puts the boot files there and points the firmware at it.
        EspReader.Read(volumes.System);

        return volumes;
    }

    private byte[] ReadRunLayout(int number)
    {
        using SafeFileHandle disk = DiskLayouts.OpenDisk(number);

        if (disk.IsInvalid)
        {
            throw new DeploymentStepException($"Disk {number}, which holds the run's Windows partition, cannot be opened (Windows error {Marshal.GetLastPInvokeError()}).");
        }

        return _layouts.ReadLayout(disk, number)
            ?? throw new DeploymentStepException($"The partitions of disk {number} cannot be read, so the run's system and recovery partitions cannot be found.");
    }

    private static uint PartitionNumber(byte[] layout, Guid id, string what, int disk)
    {
        uint? number;

        try
        {
            number = DriveLayoutReader.PartitionNumberOf(layout, id);
        }
        catch (ArgumentException exception)
        {
            throw new DeploymentStepException($"The partitions of disk {disk} cannot be read ({exception.Message}).", exception);
        }

        return number ?? throw new DeploymentStepException($"The run's {what} partition is no longer on disk {disk}, so the run cannot go on.");
    }

    private async Task<string> WriteScriptAsync(string fileName, string script, CancellationToken cancellationToken)
    {
        string path = Path.Combine(workDirectory, fileName);

        Directory.CreateDirectory(workDirectory);
        await File.WriteAllTextAsync(path, script, Encoding.ASCII, cancellationToken).ConfigureAwait(false);

        return path;
    }

    private void LogScript(string script)
    {
        foreach (string line in script.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            log.Information($"  {line}");
        }
    }

    private Task RunScriptAsync(string path, CancellationToken cancellationToken) =>
        tools.RunAsync(Path.Combine(Environment.SystemDirectory, "diskpart.exe"), ["/s", path], cancellationToken);

    // diskpart assigns letters before it exits, but the volume can still take a moment to mount.
    private async Task WaitForVolumeAsync(string root, CancellationToken cancellationToken)
    {
        long started = timeProvider.GetTimestamp();

        while (true)
        {
            if (FileSystemOf(root) is { } fileSystem)
            {
                log.Information($"Volume {root} is ready ({fileSystem}).");

                return;
            }

            if (timeProvider.GetElapsedTime(started) >= s_volumeTimeout)
            {
                throw new DeploymentStepException(
                    $"The new volume {root} did not appear within {s_volumeTimeout.TotalSeconds:0} seconds after diskpart. Check the diskpart output in the machine log.");
            }

            await Task.Delay(s_volumePollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private static unsafe string? FileSystemOf(string root)
    {
        char* name = stackalloc char[64];

        return DiskNativeMethods.GetVolumeInformation(root, null, 0, out _, out _, out _, name, 64)
            ? new string(name)
            : null;
    }
}
