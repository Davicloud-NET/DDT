// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.Deployment;

// Opens a disk by its device, with its size and sector size from /sys/block.
public sealed class LinuxRawDisks(string root, IBlockDevices devices, Action<TimeSpan>? wait = null) : IRawDisks
{
    // The kernel counts a disk's size in these, whatever its sectors are.
    public const int SizeUnit = 512;

    // The kernel's device manager opens a disk briefly when its partitions change.
    private static readonly TimeSpan s_retryAfter = TimeSpan.FromMilliseconds(250);
    private const int Attempts = 20;

    private readonly Action<TimeSpan> _wait = wait ?? Thread.Sleep;

    public IRawDisk Open(LocalDisk disk)
    {
        ArgumentNullException.ThrowIfNull(disk);

        string path = disk.Device ?? throw new DeploymentStepException($"Disk {disk.Number} has no device to open it by.");
        string block = Path.Combine(root, "sys", "block", Path.GetFileName(path));

        if (SysfsText.ReadNumber(Path.Combine(block, "size")) is not { } units
            || SysfsText.ReadNumber(Path.Combine(block, "queue", "logical_block_size")) is not { } sectorSize
            || sectorSize is <= 0 or > 1024 * 1024)
        {
            throw new DeploymentStepException($"The kernel no longer lists {path}, so the disk cannot be opened. It may have been unplugged.");
        }

        return new LinuxRawDisk(OpenAlone(path), path, units * SizeUnit, (int)sectorSize, devices);
    }

    private SafeFileHandle OpenAlone(string path)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return devices.OpenAlone(path);
            }
            catch (IOException exception) when (exception.HResult == IBlockDevices.Busy && attempt < Attempts)
            {
                _wait(s_retryAfter);
            }
            catch (IOException exception) when (exception.HResult == IBlockDevices.Busy)
            {
                throw new DeploymentStepException(
                    $"{path} is in use: a file system on it is mounted, or a RAID or volume group holds it. Nothing was written to it.",
                    exception);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new DeploymentStepException($"{path} cannot be opened for writing ({exception.Message}).", exception);
            }
        }
    }
}
