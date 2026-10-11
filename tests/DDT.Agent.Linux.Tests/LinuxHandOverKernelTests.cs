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

// The seed a hand-over leaves, as the kernel of the installed Linux reads it, and as a later start of the boot
// environment finds it once that kernel wrote to it. The image, its seed and the hand-over go through the disk's
// device onto a loop device, and the kernel mounts the result. Needs root and the right to make such devices.
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

    private int SeedPartition => Assert.IsType<WrittenSeed>(_run.Session.Seed).PartitionNumber;

    // The image and its seed, written by their steps to a loop device, and the run handed over on it. Returns the loop
    // device and the state that was staged. script does to the disk what a step between the seed and the hand-over
    // would, and with letGo the hand-over unmounts what that left mounted, as the boot environment's does.
    private async Task<(LoopDisk Loop, SequenceState State)> HandOverAsync(Action<LoopDisk>? script = null, bool letGo = false)
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

        if (script is not null)
        {
            loop.MakePartitionDevices();
            script(loop);
        }

        _handOver = new LinuxHandOver(disks, agentPath, TestAgents.Configuration, _run.Log)
        {
            LetGo = letGo ? () => new LinuxMounts("/", _run.Log).UnmountFrom(disks.Opened) : null,
        };
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

    // The look a start of the boot environment takes at the disk, through its device.
    private HandedOverSeeds Seeds() => new(Disks(), _run.Log, _run.Time);

    // What the agent's script does to the token once the unit is installed, with the installed Linux's own tools. The
    // unmount writes the volume out. With delete false, power was lost between the two commands.
    private void WipeTokenAsTheScriptDoes(LoopDisk loop, bool delete = true)
    {
        string place = Path.Combine(_folder, "installed");
        string token = Path.Combine(place, LinuxAgentFiles.SeedToken);
        loop.Mount(SeedPartition, place, "-o", "rw,umask=077");
        string size = new FileInfo(token).Length.ToString(CultureInfo.InvariantCulture);

        Assert.Equal(0, LoopDisk.Run("dd", "if=/dev/zero", $"of={token}", $"bs={size}", "count=1", "conv=notrunc,fsync").ExitCode);

        if (delete)
        {
            File.Delete(token);
        }

        loop.Unmount(place);
    }

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

    // The kernel's FAT driver writes names and chains its own way: a name in lower case without a long entry, a long
    // one with a space, a folder, a file that grew and one that is gone.
    [Fact]
    public async Task KeepsWhatAScriptWroteToTheSeedThroughTheKernel()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string place = Path.Combine(_folder, "script");

        (LoopDisk loop, _) = await HandOverAsync(script: disk =>
        {
            disk.Mount(SeedPartition, place);
            File.AppendAllText(Path.Combine(place, CloudInitSeed.UserData), "packages:\n  - htop\n");
            File.WriteAllText(Path.Combine(place, "notes.txt"), "kept");
            Directory.CreateDirectory(Path.Combine(place, "Extra Files"));
            File.WriteAllText(Path.Combine(place, "Extra Files", "A long name.cfg"), "also kept");
            File.Delete(Path.Combine(place, CloudInitSeed.NetworkConfig));
            disk.Unmount(place);
        });

        TestSeed seed = ReadSeed(loop);
        string after = Path.Combine(_folder, "seed");
        loop.Mount(seed.Partition.Number, after, "-o", "ro");

        Assert.Equal(["Extra Files", "ddt", "meta-data", "notes.txt", "user-data", "vendor-data"], Names(after));
        Assert.Equal(s_seed.UserData + "packages:\n  - htop\n", await File.ReadAllTextAsync(Path.Combine(after, CloudInitSeed.UserData), cancellationToken));
        Assert.Equal("kept", await File.ReadAllTextAsync(Path.Combine(after, "notes.txt"), cancellationToken));
        Assert.Equal("also kept", await File.ReadAllTextAsync(Path.Combine(after, "Extra Files", "A long name.cfg"), cancellationToken));
        Assert.Equal(_agent, await File.ReadAllBytesAsync(Path.Combine(after, LinuxAgentFiles.SeedAgent), cancellationToken));
        Assert.Equal(StepRunnerFixture.RunToken, await File.ReadAllTextAsync(Path.Combine(after, LinuxAgentFiles.SeedToken), cancellationToken));

        string said = Assert.Single(await _run.SentLinesAsync(), line => line.Message.StartsWith("A step changed the cloud-init seed", StringComparison.Ordinal)).Message;
        Assert.All(
            (string[])["user-data was changed", "notes.txt was added", "Extra Files/A long name.cfg was added", "network-config was removed"],
            change => Assert.Contains(change, said, StringComparison.Ordinal));
    }

    // Taking the run off again leaves what the script made of the seed, as the kernel reads it.
    [Fact]
    public async Task TakesTheRunOffASeedAScriptChangedAndLeavesTheScriptsFiles()
    {
        string place = Path.Combine(_folder, "script");
        (LoopDisk loop, _) = await HandOverAsync(script: disk =>
        {
            disk.Mount(SeedPartition, place);
            File.WriteAllText(Path.Combine(place, "notes.txt"), "kept");
            disk.Unmount(place);
        });

        _handOver?.Withdraw(_run.Session, HandOverEnd.Failed);

        TestSeed seed = ReadSeed(loop);
        string after = Path.Combine(_folder, "seed");
        loop.Mount(seed.Partition.Number, after, "-o", "ro");

        Assert.False(seed.Holds(StepRunnerFixture.RunToken));
        Assert.Equal(["meta-data", "network-config", "notes.txt", "user-data"], Names(after));
        Assert.Equal("kept", await File.ReadAllTextAsync(Path.Combine(after, "notes.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ALaterStartOfTheBootEnvironmentFindsTheRunAndItsTokenThroughTheDisksDevice()
    {
        (LoopDisk loop, SequenceState state) = await HandOverAsync();

        HandedOverSeed seed = Assert.Single(Seeds().Find([loop.Disk]));

        Assert.False(seed.IsTaken);
        Assert.Equal((StepRunnerFixture.RunToken, state.RunId, SeedPartition), (seed.Token, seed.RunId, seed.Partition.Number));
        Assert.Equal(TestRawImage.EspId, seed.SystemPartition?.PartitionId);
        Assert.DoesNotContain(await _run.SentLinesAsync(), line => line.Message.Contains("could not be looked at", StringComparison.Ordinal));
    }

    // The kernel's FAT driver marks the entry deleted and frees its cluster, which DDT's reader has to take as gone.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnceTheInstalledLinuxWipedTheTokenTheRunCountsAsTakenOver(bool deleted)
    {
        (LoopDisk loop, SequenceState state) = await HandOverAsync();
        WipeTokenAsTheScriptDoes(loop, deleted);

        HandedOverSeed seed = Assert.Single(Seeds().Find([loop.Disk]));

        Assert.True(seed.IsTaken);
        Assert.Null(seed.Token);
        Assert.Equal(state.RunId, seed.RunId);
        Assert.False(ReadSeed(loop).Holds(StepRunnerFixture.RunToken));
    }

    // A start over at the machine, or a run the server no longer knows: the seed is the administrator's again, and the
    // installed Linux still finds it by its label and serial number.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TakesTheRunOffASeedTheInstalledLinuxWroteToAndTheKernelReadsTheRest(bool taken)
    {
        (LoopDisk loop, _) = await HandOverAsync();
        uint serial = ReadSeed(loop).Volume.SerialNumber;

        if (taken)
        {
            WipeTokenAsTheScriptDoes(loop);
        }

        Assert.True(Seeds().TakeOff(Assert.Single(Seeds().Find([loop.Disk]))));

        TestSeed seed = ReadSeed(loop);
        Assert.False(seed.Holds(StepRunnerFixture.RunToken));
        Assert.Empty(Seeds().Find([loop.Disk]));
        string after = Path.Combine(_folder, "seed");
        loop.Mount(seed.Partition.Number, after, "-o", "ro");
        Assert.Equal(["meta-data", "network-config", "user-data"], Names(after));
        Assert.Equal(s_seed.UserData, await File.ReadAllTextAsync(Path.Combine(after, CloudInitSeed.UserData), TestContext.Current.CancellationToken));

        string device = loop.PartitionDevice(seed.Partition.Number);
        Assert.Equal((0, CloudInitSeed.Label), LoopDisk.Run("blkid", "-p", "-o", "value", "-s", "LABEL", device));
        Assert.Equal((0, string.Create(CultureInfo.InvariantCulture, $"{serial >> 16:X4}-{serial & 0xFFFF:X4}")), LoopDisk.Run("blkid", "-p", "-o", "value", "-s", "UUID", device));
    }

    // The same sequence without steps in the installed Linux ends well, because the restart unmounts. So does this one.
    [Fact]
    public async Task UnmountsWhatAScriptLeftMountedBeforeItTakesTheDiskAlone()
    {
        string place = Path.Combine(_folder, "script");

        (LoopDisk loop, _) = await HandOverAsync(
            script: disk =>
            {
                disk.Mount(SeedPartition, place);
                File.WriteAllText(Path.Combine(place, "notes.txt"), "kept");
            },
            letGo: true);

        TestSeed seed = ReadSeed(loop);
        Assert.Equal("kept", seed.Text("notes.txt"));
        Assert.Equal(StepRunnerFixture.RunToken, seed.Text(LinuxAgentFiles.SeedToken));
        Assert.DoesNotContain($" {place} ", await File.ReadAllTextAsync("/proc/self/mounts", TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Contains(
            await _run.SentLinesAsync(),
            line => line.Message == $"Unmounted {place}, which was still mounted from {loop.PartitionDevice(seed.Partition.Number)}, before the restart.");
    }

    [Fact]
    public async Task WithoutThatADiskWithAMountedPartitionCannotBeTaken()
    {
        string place = Path.Combine(_folder, "script");

        DeploymentStepException failure = await Assert.ThrowsAsync<DeploymentStepException>(
            () => HandOverAsync(script: disk => disk.Mount(SeedPartition, place)));

        Assert.EndsWith("is in use: a file system on it is mounted, or a RAID or volume group holds it. Nothing was written to it.", failure.Message, StringComparison.Ordinal);
        Assert.False(_run.Session.Seed?.HoldsRun);
    }

    [Fact]
    public async Task TheKernelReadsASeedTheRunWasTakenOffAsItsStepWroteIt()
    {
        (LoopDisk loop, _) = await HandOverAsync();
        Assert.True(ReadSeed(loop).Holds(StepRunnerFixture.RunToken));

        _handOver?.Withdraw(_run.Session, HandOverEnd.Failed);

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
