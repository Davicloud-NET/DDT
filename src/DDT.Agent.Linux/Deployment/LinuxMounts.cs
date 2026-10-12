// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.Deployment;

// The file systems mounted from a disk, as the kernel lists them, and their unmounting before the machine restarts
// or the agent opens a disk alone. unmount returns 0 or the error.
public sealed class LinuxMounts(string root, AgentLog log, Func<string, int>? unmount = null)
{
    public const string TheRestart = "the restart";

    public const string TheHandOver = "the hand-over writes the cloud-init seed";

    private readonly Func<string, int> _unmount = unmount ?? Unmount;

    // A script may have left a partition mounted, of any disk the agent opened, and the kernel restarts without
    // unmounting. What lies deepest goes first. A failure is only said: what comes after before happens anyway.
    public void UnmountFrom(IReadOnlyCollection<string> disks, string before = TheRestart)
    {
        ArgumentNullException.ThrowIfNull(disks);

        foreach ((string device, string target) in Mounted(disks).OrderByDescending(mount => mount.Target.Length))
        {
            int error = _unmount(target);

            if (error == 0)
            {
                log.Information($"Unmounted {target}, which was still mounted from {device}, before {before}.");
            }
            else
            {
                log.Warning($"{target} is still mounted from {device} and could not be unmounted (error {error}), so its file system is not closed before {before}.");
            }
        }
    }

    // The device and the place of everything mounted from one of the disks or from a partition of one.
    public IReadOnlyList<(string Device, string Target)> Mounted(IReadOnlyCollection<string> disks)
    {
        ArgumentNullException.ThrowIfNull(disks);

        List<(string Device, string Target)> found = [];

        foreach (string line in Lines())
        {
            string[] fields = line.Split(' ');

            if (fields.Length >= 2 && disks.Any(disk => IsOn(MountList.Unescape(fields[0]), disk)))
            {
                found.Add((MountList.Unescape(fields[0]), MountList.Unescape(fields[1])));
            }
        }

        return found;
    }

    private string[] Lines()
    {
        try
        {
            return File.ReadAllLines(Path.Combine(root, "proc", "self", "mounts"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"The kernel's list of what is mounted cannot be read ({exception.Message}), so nothing is unmounted.");

            return [];
        }
    }

    // The kernel shows each partition as a folder of its disk in /sys/block.
    private bool IsOn(string device, string disk)
    {
        string name = Path.GetFileName(device);
        string diskName = Path.GetFileName(disk);

        return device.StartsWith("/dev/", StringComparison.Ordinal)
            && (name == diskName || Directory.Exists(Path.Combine(root, "sys", "block", diskName, name)));
    }

    private static int Unmount(string target) =>
        MountNativeMethods.umount2(target, 0) == 0 ? 0 : Marshal.GetLastPInvokeError();
}
