// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

// A file as a disk the kernel knows: a loop device, with a device for each of its partitions. A container has no
// device manager that would make those, so they are made from what the kernel says of each. Needs root and the right
// to make such devices. Attach skips the test without them.
internal sealed class LoopDisk : IDisposable
{
    private readonly List<string> _nodes = [];
    private readonly List<string> _mounted = [];

    private LoopDisk(string device, long sizeBytes, int partitions)
    {
        Device = device;
        Disk = new LocalDisk(0, null, sizeBytes, StorageBusType.Virtual, partitions) { Device = device };
    }

    public string Device { get; }

    public LocalDisk Disk { get; }

    // For a test to call before it makes the file, so it makes none where it then skips.
    public static void SkipUnlessLinux() => Assert.SkipUnless(OperatingSystem.IsLinux(), "This test calls the Linux kernel.");

    public static LoopDisk Attach(string file, int partitions)
    {
        SkipUnlessLinux();

        (int exitCode, string output) = Run("losetup", "--find", "--show", "--partscan", file);
        Assert.SkipUnless(exitCode == 0, $"This system gives the tests no loop device ({output}).");

        LoopDisk disk = new(output, new FileInfo(file).Length, partitions);
        disk.MakePartitionDevices();

        return disk;
    }

    public static (int ExitCode, string Output) Run(string tool, params string[] arguments)
    {
        try
        {
            using Process process = Process.Start(new ProcessStartInfo(tool, arguments) { RedirectStandardOutput = true, RedirectStandardError = true })
                ?? throw new InvalidOperationException($"{tool} did not start.");
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();

            return (process.ExitCode, output.Trim());
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            return (-1, exception.Message);
        }
    }

    public string PartitionDevice(int number) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Device}p{number}");

    // For the partitions the kernel knows now, which change with the table.
    public void MakePartitionDevices()
    {
        string name = Path.GetFileName(Device);

        foreach (string partition in Directory.GetDirectories(Path.Combine("/sys/block", name), name + "p*"))
        {
            string node = Path.Combine("/dev", Path.GetFileName(partition));
            string[] numbers = File.ReadAllText(Path.Combine(partition, "dev")).Trim().Split(':');

            if (!File.Exists(node) && Run("mknod", node, "b", numbers[0], numbers[1]).ExitCode == 0)
            {
                _nodes.Add(node);
            }
        }
    }

    // Skips the test where the system cannot mount the partition's FAT file system.
    public void Mount(int partition, string place, params string[] options)
    {
        Directory.CreateDirectory(place);
        (int exitCode, string output) = Run("mount", ["-t", "vfat", .. options, PartitionDevice(partition), place]);
        Assert.SkipUnless(exitCode == 0, $"This system cannot mount partition {partition} of the test's disk ({output}).");
        _mounted.Add(place);
    }

    // What a script does that mounted a partition for a while.
    public void Unmount(string place)
    {
        (int exitCode, string output) = Run("umount", place);
        Assert.True(exitCode == 0, output);
        _mounted.Remove(place);
    }

    public void Dispose()
    {
        foreach (string place in _mounted)
        {
            Run("umount", place);
        }

        Run("losetup", "--detach", Device);

        foreach (string node in _nodes)
        {
            File.Delete(node);
        }
    }
}
