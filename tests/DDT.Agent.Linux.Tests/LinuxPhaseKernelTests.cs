// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Text;
using DDT.Agent.Deployment;
using DDT.Agent.LinuxPhase;
using DDT.Agent.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Disks;
using Xunit;

namespace DDT.Agent.Tests;

// What the agent in the installed Linux asks of the kernel itself: it mounts the seed a hand-over built, takes the
// token and its own files off it, and leaves alone what is mounted in a folder it deletes. The seed is a loop device.
// Needs root and the right to mount, and skips without them. A CIFS mount of a real server is for a run on a machine.
[Trait("Category", "LinuxKernel")]
public sealed class LinuxPhaseKernelTests : IDisposable
{
    private const string Token = "kernel-test-run-token-0123456789abcdefghij";

    private const string State = "{\"runId\":\"0193a4b2-0000-7000-8000-0000000000f1\",\"phase\":\"Linux\"}";

    private readonly string _folder = Directory.CreateTempSubdirectory("ddt-linux-phase-").FullName;
    private readonly StringWriter _console = new();
    private readonly AgentLog _log;
    private readonly LibcMounts _mounts = new();
    private readonly List<string> _mounted = [];
    private LoopDisk? _loop;

    public LinuxPhaseKernelTests() => _log = new AgentLog(new ImmediateTimeProvider(), _console);

    public void Dispose()
    {
        foreach (string place in _mounted)
        {
            _mounts.Unmount(place, detach: true);
        }

        _loop?.Dispose();
        Directory.Delete(_folder, recursive: true);
        _console.Dispose();
    }

    // A folder that stands in for / and shows the agent this test's seed alone, among the machine's own partitions
    // and the seeds of the tests beside it.
    private LinuxAgentPaths Paths => new(Path.Combine(_folder, "system"));

    // The seed as a hand-over leaves it, with the token the script would have wiped, on a loop device of its own.
    private LoopDisk Seed()
    {
        LoopDisk.SkipUnlessLinux();

        byte[] agent = new byte[300_000];
        new Random(11).NextBytes(agent);
        byte[] volume = CloudInitSeed.Build(
            [
                .. CloudInitSeed.FilesOf(new CloudInitSeedFiles("instance-id: a\n", "#cloud-config\nusers:\n  - default\n", null)),
                .. LinuxAgentFiles.ForSeed(agent, Encoding.UTF8.GetBytes("{}"), Encoding.UTF8.GetBytes(State), Encoding.ASCII.GetBytes(Token)),
            ],
            0x1234ABCD,
            new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc),
            0);
        string file = Path.Combine(_folder, "seed.img");
        File.WriteAllBytes(file, volume);

        LoopDisk loop = LoopDisk.Attach(file, partitions: 0);
        _loop = loop;
        string name = Path.GetFileName(loop.Device);
        Directory.CreateDirectory(Paths.At("/sys/class/block/" + name));
        File.WriteAllText(Paths.At($"/sys/class/block/{name}/partition"), "1\n");
        Directory.CreateDirectory(Paths.At("/dev"));
        File.CreateSymbolicLink(Paths.At("/dev/" + name), loop.Device);

        return loop;
    }

    // The whole volume as the device holds it now, also what no file holds any more.
    private static (FatVolume Volume, byte[] Bytes) Read(LoopDisk loop)
    {
        byte[] bytes = File.ReadAllBytes(loop.Device);

        return (FatVolume.Open(new MemoryStream(bytes), 0, bytes.Length), bytes);
    }

    private static bool Holds(byte[] bytes, string text) => bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text)) >= 0;

    [Fact]
    public void TakesATokenOffTheSeedSoThatNoByteOfItStaysOnTheDisk()
    {
        LoopDisk loop = Seed();
        Assert.True(Holds(Read(loop).Bytes, Token));

        bool repaired = new SeedTokenRepair(new LinuxSeeds(_mounts, Paths, _log), _log).Run();

        Assert.True(repaired, _console.ToString());
        (FatVolume volume, byte[] bytes) = Read(loop);
        Assert.False(Holds(bytes, Token));
        Assert.Equal(["ddt-agent", "agent.json", "state.json"], volume.List(LinuxAgentFiles.SeedFolder).Select(entry => entry.Name));
        Assert.Equal(["meta-data", "user-data", "vendor-data", "ddt"], volume.List("").Select(entry => entry.Name));
        Assert.Empty(MountsBelow.Of(_folder));
        Assert.Contains(SeedTokenRepair.Repaired, _console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRemovalLeavesTheSeedAsTheAdministratorsStepWroteIt()
    {
        LoopDisk loop = Seed();
        RecordingToolRunner tools = new();

        await new LinuxAgentRemoval(Paths, new LinuxSeeds(_mounts, Paths, _log), tools, _log).RemoveAsync(TestContext.Current.CancellationToken);

        (FatVolume volume, byte[] bytes) = Read(loop);
        Assert.Equal(["meta-data", "user-data"], volume.List("").Select(entry => entry.Name));
        Assert.False(Holds(bytes, Token));
        Assert.False(Holds(bytes, "0193a4b2-0000-7000-8000-0000000000f1"));
        Assert.Equal(["systemctl disable ddt-agent.service", "systemctl daemon-reload"], tools.Calls);
        Assert.Empty(MountsBelow.Of(_folder));
        Assert.DoesNotContain("WARN", _console.ToString(), StringComparison.Ordinal);
    }

    // A script left a disk mounted in its package's folder. Deleting the folder would delete the disk's files.
    [Fact]
    public void LeavesAFolderInWhichSomethingIsStillMounted()
    {
        LoopDisk loop = Seed();
        string package = Path.Combine(_folder, "packages", "0193");
        string place = Path.Combine(package, "my disk");
        Directory.CreateDirectory(place);
        Assert.Equal(0, _mounts.Mount(loop.Device, place, LinuxSeeds.FileSystem, LinuxSeeds.Options));
        _mounted.Add(place);

        Leftovers.Delete(package, _log);

        Assert.True(File.Exists(Path.Combine(place, CloudInitSeed.UserData)));
        Assert.Contains($"{package} was not deleted, because {place} is still mounted in it and would lose its files.", _console.ToString(), StringComparison.Ordinal);

        Assert.Equal(0, _mounts.Unmount(place, detach: false));
        Leftovers.Delete(package, _log);

        Assert.False(Directory.Exists(package));
        Assert.Equal(["meta-data", "user-data", "vendor-data", "ddt"], Read(loop).Volume.List("").Select(entry => entry.Name));
    }

    // The call itself, with a password that holds commas. Nothing listens here, so the kernel answers that the port
    // is closed, or that it has no CIFS where a container cannot load the module.
    [Fact]
    public void AsksTheKernelForACifsMountAndGetsItsErrorNumber()
    {
        LoopDisk.SkipUnlessLinux();
        Assert.SkipUnless(Environment.UserName == "root", "Only root may mount.");
        string place = Path.Combine(_folder, "share");
        Directory.CreateDirectory(place);
        string options = CifsOptions.For(IPAddress.Loopback, "nobody", "made,up, password") ?? "";

        int error = _mounts.Mount("//127.0.0.1/ddt-no-such-share", place, LinuxShareConnector.FileSystem, options);

        _mounted.Add(place);
        Assert.Contains(error, (int[])[19, 111]);
        Assert.Equal(22, _mounts.Unmount(place, detach: true));
    }
}
