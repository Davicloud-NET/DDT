// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

// The calls a disk's device needs, against a device the kernel makes of a file: a loop device with the test image on
// it. A file alone shows none of this. Needs root and the right to make such devices, and skips without them.
[Trait("Category", "LinuxKernel")]
public sealed class LinuxLoopDeviceTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("ddt-loop-").FullName;
    private readonly StringWriter _console = new();
    private readonly List<string> _nodes = [];
    private string? _device;

    public void Dispose()
    {
        if (_device is not null)
        {
            Run("umount", Path.Combine(_folder, "esp"));
            Run("losetup", "--detach", _device);
        }

        foreach (string node in _nodes)
        {
            File.Delete(node);
        }

        Directory.Delete(_folder, recursive: true);
    }

    private AgentLog Log() => new(new ImmediateTimeProvider(), _console);

    private static (int ExitCode, string Output) Run(string tool, params string[] arguments)
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

    // The test image as a disk, with a device for each of its partitions. A container has no device manager that
    // would make those, so the test makes them from what the kernel says of each.
    private LocalDisk Attach()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "This test calls the Linux kernel.");

        string file = Path.Combine(_folder, "disk.img");
        File.WriteAllBytes(file, new TestRawImage().Disk);
        (int exitCode, string output) = Run("losetup", "--find", "--show", "--partscan", file);
        Assert.SkipUnless(exitCode == 0, $"This system gives the tests no loop device ({output}).");
        _device = output;
        string name = Path.GetFileName(_device);

        foreach (string partition in Directory.GetDirectories(Path.Combine("/sys/block", name), name + "p*"))
        {
            string node = Path.Combine("/dev", Path.GetFileName(partition));
            string[] numbers = File.ReadAllText(Path.Combine(partition, "dev")).Trim().Split(':');

            if (!File.Exists(node) && Run("mknod", node, "b", numbers[0], numbers[1]).ExitCode == 0)
            {
                _nodes.Add(node);
            }
        }

        return new LocalDisk(0, null, TestRawImage.Sectors * 512, StorageBusType.Virtual, 2) { Device = _device };
    }

    private string MountSystemPartition()
    {
        string place = Directory.CreateDirectory(Path.Combine(_folder, "esp")).FullName;
        (int exitCode, string output) = Run("mount", "-t", "vfat", _device + "p1", place);
        Assert.SkipUnless(exitCode == 0, $"This system cannot mount the image's EFI system partition ({output}).");

        return place;
    }

    [Fact]
    public void ADiskHasTheSizeAndTheSectorsTheKernelShowsAndIsWrittenThrough()
    {
        LocalDisk disk = Attach();
        byte[] written = [.. Enumerable.Range(0, 4096).Select(index => (byte)(index * 3))];
        byte[] read = new byte[4096];

        using (IRawDisk raw = new LinuxRawDisks("/", new KernelBlockDevices()).Open(disk))
        {
            Assert.Equal((TestRawImage.Sectors * 512, 512), (raw.Length, raw.SectorSize));
            raw.Write(6 * 1024 * 1024, written);
            raw.Flush();
            raw.Read(6 * 1024 * 1024, read);
        }

        Assert.Equal(written, read);
    }

    // What the agent relies on before it erases: a disk with a mounted file system cannot be had alone.
    [Fact]
    public void TheKernelRefusesADiskWhileAPartitionOfItIsMountedAndGivesItOnceThatIsUnmounted()
    {
        LocalDisk disk = Attach();
        string place = MountSystemPartition();
        LinuxRawDisks disks = new("/", new KernelBlockDevices(), _ => { });

        IOException busy = Assert.Throws<IOException>(() => new KernelBlockDevices().OpenAlone(disk.Device ?? ""));
        DeploymentStepException refusal = Assert.Throws<DeploymentStepException>(() => disks.Open(disk));

        Assert.Equal(IBlockDevices.Busy, busy.HResult);
        Assert.Equal($"{disk.Device} is in use: a file system on it is mounted, or a RAID or volume group holds it. Nothing was written to it.", refusal.Message);

        // The same unmounting as before a restart, with the kernel's own list and its own call.
        LinuxMounts mounts = new("/", Log());
        Assert.Equal([(disk.Device + "p1", place)], mounts.Mounted([disk.Device ?? ""]));

        mounts.UnmountFrom([disk.Device ?? ""]);

        Assert.Empty(mounts.Mounted([disk.Device ?? ""]));
        Assert.Contains($"Unmounted {place}, which was still mounted from {disk.Device}p1, before the restart.", _console.ToString(), StringComparison.Ordinal);
        disks.Open(disk).Dispose();
    }

    [Fact]
    public async Task ErasingTheTableMakesTheKernelForgetThePartitions()
    {
        LocalDisk disk = Attach();
        string partition = Path.Combine("/sys/block", Path.GetFileName(disk.Device ?? ""), Path.GetFileName(disk.Device) + "p1");
        Assert.True(Directory.Exists(partition), "The kernel did not read the image's partition table.");
        LinuxRawDisks rawDisks = new("/", new KernelBlockDevices());

        IReadOnlyList<Guid> erased = await new LinuxDisks("/", rawDisks, Log()).CleanAsync(disk, TestContext.Current.CancellationToken);

        Assert.Equal([TestRawImage.EspId], erased);
        Assert.False(Directory.Exists(partition));
        Assert.DoesNotContain("did not read the partition table", _console.ToString(), StringComparison.Ordinal);
    }
}
