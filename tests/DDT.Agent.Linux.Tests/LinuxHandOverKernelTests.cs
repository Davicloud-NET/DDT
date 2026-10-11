// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Disks;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// The seed a hand-over leaves, as the kernel of the installed Linux reads it. The image, its seed and the hand-over go
// through the disk's device onto a loop device, and the kernel mounts the result. Needs root and the right to make
// such devices, and skips without them.
[Trait("Category", "LinuxKernel")]
public sealed class LinuxHandOverKernelTests : IDisposable
{
    private const long DiskBytes = 96L * 1024 * 1024;

    private static readonly WriteRawImageStep s_write = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000d2"),
        Name = "Write the disk",
        ImageId = TestRawImage.ImageId,
    };

    private static readonly WriteCloudInitSeedStep s_seed = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000d3"),
        Name = "Seed",
        MetaData = "instance-id: \"{{SmbiosUuid}}\"\nlocal-hostname: \"{{ComputerName}}\"\n",
        UserData = "#cloud-config\nusers:\n  - default\n",
        NetworkConfig = "version: 2\n",
    };

    private static readonly RunScriptStep s_installed = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000d4"),
        Name = "In the installed Linux",
        Phase = SequencePhase.Linux,
        Interpreter = ScriptInterpreter.Shell,
        Script = "echo \"$DDT_PHASE\"",
        RebootExitCodes = [194],
    };

    private readonly string _folder = Directory.CreateTempSubdirectory("ddt-seed-").FullName;
    private readonly TestRawImage _image = new();
    private readonly StepRunnerFixture _run;
    private readonly byte[] _agent = new byte[2_500_000];
    private LoopDisk? _loop;
    private LinuxHandOver? _handOver;

    public LinuxHandOverKernelTests()
    {
        _run = new StepRunnerFixture([s_write, s_seed, s_installed], [_image.RunImage()]);
        _image.Serve(_run.Server);
        new Random(5).NextBytes(_agent);
    }

    public void Dispose()
    {
        _loop?.Dispose();
        _run.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private static LinuxRawDisks Disks() => new("/", new KernelBlockDevices());

    // The image and its seed, written by their steps to a loop device, and the run handed over on it. Returns the loop
    // device and the state that was staged.
    private async Task<(LoopDisk Loop, SequenceState State)> HandOverAsync()
    {
        LoopDisk.SkipUnlessLinux();

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string file = Path.Combine(_folder, "disk.img");

        await using (FileStream empty = File.Create(file))
        {
            empty.SetLength(DiskBytes);
        }

        string agentPath = Path.Combine(_folder, "ddt-agent");
        await File.WriteAllBytesAsync(agentPath, _agent, cancellationToken);
        LoopDisk loop = LoopDisk.Attach(file, partitions: 0);
        _loop = loop;
        _run.Session.Disk = loop.Disk;
        LinuxRawDisks disks = Disks();

        IReadOnlyDictionary<string, string> image = (await new WriteRawImageStepRunner(_run.Tools, disks, _run.Downloads, _run.Session, _run.Log)
            .RunAsync(s_write, _run.Context(SequencePhase.LinuxBoot), cancellationToken)).Outputs!;
        StepResult seeded = await new WriteCloudInitSeedStepRunner(disks, _run.Session, _run.Log, _run.Time)
            .RunAsync(s_seed, _run.Context(SequencePhase.LinuxBoot, image), cancellationToken);
        SequenceState state = SequenceStates.Start(StepRunnerFixture.RunId, _run.Session.Run.Sequence) with
        {
            Phase = SequencePhase.Linux,
            Steps = [new(s_write.Id, StepState.Done, null, 1), new(s_seed.Id, StepState.Done, null, 1), new(s_installed.Id, StepState.Pending, null)],
            Variables = new Dictionary<string, string>([.. image, .. seeded.Outputs!], StringComparer.Ordinal),
            Cursor = new NodeCursor(s_installed.Id, false),
        };

        _handOver = new LinuxHandOver(disks, agentPath, TestAgents.Configuration, _run.Log);
        _handOver.Stage(_run.Session, state);
        loop.MakePartitionDevices();

        return (loop, state);
    }

    // Through the disk's device, which the agent has alone, so before the kernel mounts anything of it.
    private static TestSeed ReadSeed(LoopDisk loop)
    {
        using IRawDisk raw = Disks().Open(loop.Disk);

        return TestSeed.Read(raw);
    }

    private static string?[] Names(string folder) => [.. Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal)];

    [Fact]
    public async Task TheKernelReadsTheSeedWithTheAgentOnItAsDdtsOwnReaderDoes()
    {
        (LoopDisk loop, SequenceState state) = await HandOverAsync();
        TestSeed seed = ReadSeed(loop);
        string place = Path.Combine(_folder, "seed");
        string folder = Path.Combine(place, LinuxAgentFiles.SeedFolder);

        loop.Mount(seed.Partition.Number, place, "-o", "ro");

        // The long names, in the case the script asks for them.
        Assert.Equal(["ddt", "meta-data", "network-config", "user-data", "vendor-data"], Names(place));
        Assert.Equal(["agent.json", "ddt-agent", "state.json", "token"], Names(folder));
        Assert.Equal(_agent, await File.ReadAllBytesAsync(Path.Combine(folder, "ddt-agent"), TestContext.Current.CancellationToken));
        Assert.Equal(VendorDataScript.Bytes(), await File.ReadAllBytesAsync(Path.Combine(place, CloudInitSeed.VendorData), TestContext.Current.CancellationToken));
        Assert.Equal(RunFiles.StateContent(state), await File.ReadAllBytesAsync(Path.Combine(folder, "state.json"), TestContext.Current.CancellationToken));
        Assert.Equal(seed.File(LinuxAgentFiles.SeedConfiguration), await File.ReadAllBytesAsync(Path.Combine(folder, "agent.json"), TestContext.Current.CancellationToken));
        Assert.Equal(StepRunnerFixture.RunToken, await File.ReadAllTextAsync(Path.Combine(folder, "token"), TestContext.Current.CancellationToken));
        Assert.Equal(seed.Text(CloudInitSeed.UserData), await File.ReadAllTextAsync(Path.Combine(place, CloudInitSeed.UserData), TestContext.Current.CancellationToken));
        Assert.Equal("version: 2\n", await File.ReadAllTextAsync(Path.Combine(place, CloudInitSeed.NetworkConfig), TestContext.Current.CancellationToken));

        // What df shows is what the builder left free.
        Assert.Equal(seed.Volume.FreeBytes, VolumeSpace.Of(place).Available);

        // cloud-init and the script find the volume by its label, and the serial number is the one the seed step gave it.
        string device = loop.PartitionDevice(seed.Partition.Number);
        uint serial = seed.Volume.SerialNumber;
        Assert.Equal((0, CloudInitSeed.Label), LoopDisk.Run("blkid", "-p", "-o", "value", "-s", "LABEL", device));
        Assert.Equal((0, string.Create(CultureInfo.InvariantCulture, $"{serial >> 16:X4}-{serial & 0xFFFF:X4}")), LoopDisk.Run("blkid", "-p", "-o", "value", "-s", "UUID", device));
        Assert.Equal(_run.Session.Seed?.SerialNumber, serial);

        // The shell reads the script as one. It is not run: on a system with systemd it would install the agent.
        Assert.Equal((0, ""), LoopDisk.Run("sh", "-n", Path.Combine(place, CloudInitSeed.VendorData)));
    }

    [Fact]
    public async Task TheKernelReadsASeedTheRunWasTakenOffAsItsStepWroteIt()
    {
        (LoopDisk loop, _) = await HandOverAsync();
        Assert.True(ReadSeed(loop).Holds(StepRunnerFixture.RunToken));

        _handOver?.Withdraw(_run.Session);

        TestSeed seed = ReadSeed(loop);
        string place = Path.Combine(_folder, "seed");
        loop.Mount(seed.Partition.Number, place, "-o", "ro");

        Assert.False(seed.Holds(StepRunnerFixture.RunToken));
        Assert.False(_run.Session.Seed?.HoldsRun);
        Assert.Equal(["meta-data", "network-config", "user-data"], Names(place));
        Assert.Equal(seed.Text(CloudInitSeed.MetaData), await File.ReadAllTextAsync(Path.Combine(place, CloudInitSeed.MetaData), TestContext.Current.CancellationToken));
        Assert.Equal(seed.Volume.FreeBytes, VolumeSpace.Of(place).Available);
        Assert.Equal((0, CloudInitSeed.Label), LoopDisk.Run("blkid", "-p", "-o", "value", "-s", "LABEL", loop.PartitionDevice(seed.Partition.Number)));
    }
}
