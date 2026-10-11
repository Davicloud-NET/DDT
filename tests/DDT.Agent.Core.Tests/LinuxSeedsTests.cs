// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.LinuxPhase;
using DDT.Agent.Sequences;
using DDT.Core.CloudInit;
using Xunit;

namespace DDT.Agent.Tests;

// Finding and mounting the cloud-init seeds, over a folder that stands in for /, with files as partitions and the
// kernel's mount call faked. The kernel itself mounts a handed-over seed in the tests of the agent for Linux.
public sealed class LinuxSeedsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ddt-linux-seeds-{Guid.NewGuid():N}");
    private readonly FakeMounts _mounts = new();
    private readonly StringWriter _console = new();
    private readonly LinuxAgentPaths _paths;

    public LinuxSeedsTests() => _paths = new LinuxAgentPaths(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        _console.Dispose();
    }

    private LinuxSeeds Seeds() => new(_mounts, _paths, new AgentLog(new ImmediateTimeProvider(), _console));

    // A partition as the kernel lists it, holding content.
    private string Partition(string name, byte[] content)
    {
        string device = _paths.At("/dev/" + name);
        Directory.CreateDirectory(Path.GetDirectoryName(device)!);
        File.WriteAllBytes(device, content);
        Directory.CreateDirectory(_paths.At("/sys/class/block/" + name));
        File.WriteAllText(_paths.At($"/sys/class/block/{name}/partition"), "2\n");

        return device;
    }

    // The first megabyte of a seed as DDT builds it, which holds its boot sector.
    private static byte[] Seed() =>
        CloudInitSeed.Build(new CloudInitSeedFiles("instance-id: a\n", "#cloud-config\n", null), 0x1234ABCD, new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc), 2048)[..(1024 * 1024)];

    [Fact]
    public void MountsEveryPartitionLabelledAsASeedAndNoOther()
    {
        string seed = Partition("nvme0n1p2", Seed());
        string second = Partition("sdb1", Seed());
        Partition("nvme0n1p1", new byte[4096]);
        Partition("nvme0n1p3", [1, 2, 3]);
        Directory.CreateDirectory(_paths.At("/sys/class/block/nvme0n1"));
        List<string> visited = [];

        int mounted = Seeds().Each(folder => visited.Add(folder));

        Assert.Equal(2, mounted);
        Assert.Equal([_paths.SeedMount, _paths.SeedMount], visited);
        Assert.Equal(
            [
                $"mount vfat {seed} at {_paths.SeedMount}",
                $"unmount {_paths.SeedMount}",
                $"mount vfat {second} at {_paths.SeedMount}",
                $"unmount {_paths.SeedMount}",
            ],
            _mounts.Calls);
        Assert.All(_mounts.Options, options => Assert.Equal("umask=077", options));
        Assert.False(Directory.Exists(_paths.SeedMount));
    }

    // A seed an image brought as a CD image is labelled the same and is no FAT volume.
    [Fact]
    public void GoesOnToTheNextSeedWhenOneCannotBeMountedOrItsVisitFails()
    {
        string foreign = Partition("sda1", Seed());
        string ours = Partition("sda2", Seed());
        _mounts.Errors[foreign] = 22;
        List<string> visited = [];

        Assert.Equal(1, Seeds().Each(visited.Add));
        Assert.Throws<IOException>(() => Seeds().Each(_ => throw new IOException("The seed is full.")));

        Assert.Single(visited);
        Assert.Equal($"unmount {_paths.SeedMount}", _mounts.Calls[^1]);
        Assert.Contains($"mount vfat {ours} at {_paths.SeedMount}", _mounts.Calls);
    }

    [Fact]
    public void FindsNoSeedOnASystemThatShowsNone()
    {
        Assert.Equal(0, Seeds().Each(_ => Assert.Fail("Nothing is there to visit.")));
        Assert.Empty(_mounts.Calls);
    }

    // What the kernel lists as mounted, with a space in a place written as \040.
    [Fact]
    public void KnowsWhatIsMountedBelowAFolderFromTheKernelsList()
    {
        string[] lines =
        [
            "/dev/nvme0n1p1 / ext4 rw,relatime 0 0",
            "tmpfs /run tmpfs rw,nosuid,nodev,noexec 0 0",
            @"/dev/sdb1 /var/lib/ddt/packages/0193/my\040disk ext4 rw 0 0",
            "//files/drivers /var/lib/ddt/packages/0193/share cifs rw 0 0",
            "/dev/sdc1 /var/lib/ddt-other ext4 rw 0 0",
            "/dev/sdd1 /var/lib/ddt/packages ext4 rw 0 0",
        ];

        Assert.Equal(
            ["/var/lib/ddt/packages/0193/my disk", "/var/lib/ddt/packages/0193/share"],
            MountsBelow.In(lines, "/var/lib/ddt/packages/"));
        Assert.Equal(3, MountsBelow.In(lines, "/var/lib/ddt").Count);
        Assert.Empty(MountsBelow.In(lines, "/var/lib/ddt/scripts"));
    }
}
