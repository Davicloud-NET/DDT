// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Facts;
using Xunit;

namespace DDT.Agent.Tests;

// What only a Linux kernel answers: the calls into libc, links as /sys has them, and the machine the tests run on.
// A file stands in for a disk, which the kernel opens the same way but has no partition table for.
[Trait("Category", "LinuxKernel")]
public sealed class LinuxKernelTests : IDisposable
{
    private const string OnlyLinux = "This test calls the Linux kernel.";
    private const int Megabyte = 1024 * 1024;

    private readonly LinuxTree _tree = new();

    public void Dispose() => _tree.Dispose();

    // The script leaves three processes its own tree does not hold: one in a session of its own, one whose parent
    // ended at once, and one started with nohup. Each sleeps for a time no other process on the computer sleeps for.
    [Fact]
    public async Task AScriptThatIsStoppedTakesWithItWhatItStartedOutsideItsOwnTree()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), OnlyLinux);
        Assert.SkipUnless(LinuxStrayProcesses.Adopt(), "This kernel hands a process no orphans.");

        string time = $"86400.{Environment.ProcessId}{Random.Shared.Next(1000, 9999)}";
        string script = $"setsid sleep {time} & (sleep {time} &) ; nohup sleep {time} > /dev/null 2>&1 & echo started; sleep {time}";
        StringWriter console = new();
        AgentLog log = new(new ImmediateTimeProvider(), console);
        ToolRunner tools = new(log, TimeProvider.System, Latin1Output.Decode)
        {
            Strays = new LinuxStrayProcesses("/", log, Environment.ProcessId) { Adopts = true },
        };

        DeploymentStepException stopped = await Assert.ThrowsAsync<DeploymentStepException>(() => tools.RunForExitCodeAsync(
            "/bin/sh",
            ["-c", script],
            new ToolRunOptions(Timeout: TimeSpan.FromSeconds(3)),
            TestContext.Current.CancellationToken));

        Assert.EndsWith("so it was stopped with every process it started.", stopped.Message, StringComparison.Ordinal);
        Assert.Contains("started", console.ToString(), StringComparison.Ordinal);
        Assert.Empty(Sleeping(time));
        Assert.Contains("more of its processes, which it had started outside its own tree.", console.ToString(), StringComparison.Ordinal);
    }

    // The unit gives the agent a mask that leaves a new file to root alone. A script's files are for whom it means them.
    [Fact]
    public async Task AToolStartsWithARootShellsFileModeMaskAndTheAgentKeepsItsOwn()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), OnlyLinux);

        const int RootAlone = 0x3F;
        ToolRunner tools = new(new AgentLog(new ImmediateTimeProvider(), TextWriter.Null), TimeProvider.System, Latin1Output.Decode)
        {
            FileModeMask = FileModeMask.Set,
        };
        int before = FileModeMask.Set(RootAlone);

        try
        {
            IReadOnlyList<string> lines = await tools.RunAsync("/bin/sh", ["-c", "umask"], TestContext.Current.CancellationToken);

            Assert.Equal(["0022"], lines);
            Assert.Equal(RootAlone, FileModeMask.Set(RootAlone));
        }
        finally
        {
            FileModeMask.Set(before);
        }
    }

    // The ids of the living processes whose command line holds the text.
    private static List<int> Sleeping(string text)
    {
        List<int> found = [];

        foreach (string folder in Directory.GetDirectories("/proc"))
        {
            try
            {
                bool alive = File.ReadAllText(Path.Combine(folder, "stat")) is { } stat && stat[(stat.LastIndexOf(')') + 2)..][0] is not ('Z' or 'X');

                if (alive && File.ReadAllText(Path.Combine(folder, "cmdline")).Contains(text, StringComparison.Ordinal) && int.TryParse(Path.GetFileName(folder), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id))
                {
                    found.Add(id);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return found;
    }

    [Fact]
    public void OpensADeviceAloneAndWritesThrough()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), OnlyLinux);

        string device = _tree.Write(new byte[4 * Megabyte], "dev", "sda");
        _tree.AddDisk("sda", 4 * Megabyte, model: "Test");
        LocalDisk disk = new(0, "Test", 4 * Megabyte, StorageBusType.Sata, 0) { Device = device };
        byte[] written = [.. Enumerable.Range(0, 4096).Select(index => (byte)(index * 7))];
        byte[] read = new byte[4096];

        using (IRawDisk raw = new LinuxRawDisks(_tree.Root, new KernelBlockDevices()).Open(disk))
        {
            raw.Write(Megabyte, written);
            raw.Flush();
            raw.Read(Megabyte, read);

            // A file has no partition table for the kernel to read.
            Assert.Contains("did not read the partition table", Assert.Throws<DeploymentStepException>(raw.UpdateProperties).Message, StringComparison.Ordinal);
        }

        Assert.Equal(written, read);
        Assert.Equal(written, File.ReadAllBytes(device).AsSpan(Megabyte, 4096).ToArray());
    }

    [Fact]
    public void SaysWhatTheKernelSaysOfADeviceThatIsNotThere()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), OnlyLinux);

        IOException refusal = Assert.Throws<IOException>(() => new KernelBlockDevices().OpenAlone(_tree.PathOf("dev", "none")));

        Assert.Equal(2, refusal.HResult);
        Assert.Contains("No such file", refusal.Message, StringComparison.Ordinal);
    }

    // A file that is not immutable stays as it is, and so does one on a file system without such flags.
    [Fact]
    public void ClearingTheImmutableFlagLeavesAnOrdinaryFileAlone()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), OnlyLinux);

        string file = _tree.Write([1, 2, 3], "variable");

        ImmutableFlag.Clear(file);

        Assert.Equal([1, 2, 3], File.ReadAllBytes(file));
    }

    // /sys/block holds links into /sys/devices, and the way there names what the disk is attached to.
    [Fact]
    public async Task FollowsTheKernelsLinkToWhereADiskIsAttached()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), OnlyLinux);

        string real = _tree.PathOf("sys", "devices", "pci0000:00", "0000:00:17.0", "ata1", "host0", "target0:0:0", "0:0:0:0", "block", "sda");
        _tree.Write($"{64L * 1000 * 1000 * 1000 / 512}\n", "sys", "devices", "pci0000:00", "0000:00:17.0", "ata1", "host0", "target0:0:0", "0:0:0:0", "block", "sda", "size");
        Directory.CreateDirectory(_tree.PathOf("sys", "block"));
        Directory.CreateSymbolicLink(_tree.PathOf("sys", "block", "sda"), Path.GetRelativePath(_tree.PathOf("sys", "block"), real));
        LinuxDisks disks = new(_tree.Root, new LinuxRawDisks(_tree.Root, new KernelBlockDevices()), new AgentLog(new ImmediateTimeProvider(), TextWriter.Null));

        LocalDisk disk = Assert.Single(await disks.ListDisksAsync(TestContext.Current.CancellationToken));

        Assert.Equal((StorageBusType.Sata, _tree.PathOf("dev", "sda")), (disk.BusType, disk.Device));
    }

    [Fact]
    public void ReadsTheMemoryAndTheProcessorsOfTheMachineItRunsOn()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), OnlyLinux);

        LinuxSystemHardware hardware = new("/");

        Assert.True(hardware.UsableMemoryBytes() > 64UL * Megabyte);
        Assert.True(hardware.ProcessorCores() is { Cores: > 0 } processors && processors.LogicalProcessors >= processors.Cores);
    }

    [Fact]
    public void CountsTheFreeSpaceOfAFileSystemByAnyPathOnIt()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), OnlyLinux);

        Assert.True(new LinuxFreeSpace().AvailableBytes("/proc/self/does/not/exist") >= 0);
        Assert.True(new LinuxFreeSpace().AvailableBytes(_tree.PathOf("a", "b")) > 0);
    }
}
