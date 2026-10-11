// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Disks;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// A script between the seed step and the hand-over has the disk. What it changed on the seed is kept, and what the
// hand-over cannot keep is refused in words, before anything is written.
public sealed class LinuxHandOverChangedSeedTests : IDisposable
{
    private const string Nothing = "Nothing was written to the disk.";

    private readonly SeededRun _seeded = new();

    public void Dispose() => _seeded.Dispose();

    private StepRunnerFixture Run => _seeded.Run;

    private MemoryRawDisk Disk => _seeded.Disk;

    private static SeedFile Text(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));

    // The seed's files with user-data changed, a file added in a folder and network-config removed.
    private IReadOnlyList<SeedFile> Changed() =>
    [
        _seeded.Written()[0],
        Text(CloudInitSeed.UserData, "#cloud-config\nusers:\n  - default\npackages:\n  - htop\n"),
        Text("extra/notes.txt", "kept"),
    ];

    private async Task<DeploymentStepException> RefusedAsync(Action<SeededRun> change)
    {
        SequenceState state = await _seeded.SeededAsync();
        change(_seeded);
        int writes = Disk.Writes.Count;

        DeploymentStepException refusal = Assert.Throws<DeploymentStepException>(() => _seeded.HandOver().Stage(Run.Session, state));

        Assert.Equal(writes, Disk.Writes.Count);
        Assert.False(_seeded.Kept.HoldsRun);
        Assert.EndsWith(Nothing, refusal.Message, StringComparison.Ordinal);

        return refusal;
    }

    // Puts the table on the disk again without the seed's entry, or with it at another place.
    private static void RewriteTable(SeededRun seeded, long? seedFirstSector)
    {
        GptLayout table = seeded.Table();
        GptLayout changed = GptLayout.Create(table.DiskSectors, table.DiskId);

        foreach (GptPartition partition in table.Partitions)
        {
            bool seed = partition.Name == CloudInitSeed.Label;

            if (!seed || seedFirstSector is not null)
            {
                long first = seed ? seedFirstSector ?? partition.FirstLba : partition.FirstLba;
                changed = changed.WithPartition(partition.Type, partition.Id, partition.Name, first, first + partition.Sectors - 1);
            }
        }

        seeded.Disk.Write(changed.EntriesLba * GptLayout.SectorSize, changed.EntryArray());
        seeded.Disk.Write(GptLayout.SectorSize, changed.PrimaryHeader());
    }

    [Fact]
    public async Task KeepsWhatAScriptChangedAddedAndRemovedOnTheSeedAndSaysSo()
    {
        SequenceState state = await _seeded.SeededAsync();
        _seeded.RewriteSeed(Changed());
        int before = (await Run.SentLinesAsync()).Count;

        _seeded.HandOver().Stage(Run.Session, state);

        TestSeed seed = TestSeed.Read(Disk);
        Assert.Equal(["meta-data", "user-data", "extra", "vendor-data", "ddt"], seed.Names());
        Assert.Equal("#cloud-config\nusers:\n  - default\npackages:\n  - htop\n", seed.Text(CloudInitSeed.UserData));
        Assert.Equal("kept", seed.Text("extra/notes.txt"));
        Assert.Equal(_seeded.Agent, seed.File(LinuxAgentFiles.SeedAgent));
        Assert.Equal(StepRunnerFixture.RunToken, seed.Text(LinuxAgentFiles.SeedToken));

        AgentLogLine said = (await Run.SentLinesAsync())[before];
        Assert.Equal(
            (AgentLogLevel.Information,
                "A step changed the cloud-init seed after it was written: user-data was changed, extra/notes.txt was added, " +
                "network-config was removed. The seed keeps the files it holds now, and the agent goes beside them."),
            (said.Level, said.Message));
    }

    [Fact]
    public async Task TakingTheRunOffPutsBackWhatTheScriptLeftAndNotWhatTheStepWrote()
    {
        SequenceState state = await _seeded.SeededAsync();
        _seeded.RewriteSeed(Changed());
        byte[] changed = TestSeed.Read(Disk).Bytes;
        LinuxHandOver handOver = _seeded.HandOver();
        handOver.Stage(Run.Session, state);

        handOver.Withdraw(Run.Session);

        TestSeed after = TestSeed.Read(Disk);
        Assert.Equal(changed, after.Bytes);
        Assert.Equal(["meta-data", "user-data", "extra"], after.Names());
        Assert.False(after.Holds(StepRunnerFixture.RunToken));
        Assert.False(_seeded.Kept.HoldsRun);
    }

    // A script that only read the seed, or wrote a file back as it was, changed nothing worth a line.
    [Fact]
    public async Task SaysNothingOfASeedWhoseFilesAreAsTheStepWroteThem()
    {
        SequenceState state = await _seeded.SeededAsync();
        _seeded.RewriteSeed(_seeded.Written());
        int before = (await Run.SentLinesAsync()).Count;

        _seeded.HandOver().Stage(Run.Session, state);

        Assert.All((await Run.SentLinesAsync())[before..], line => Assert.DoesNotContain("changed", line.Message, StringComparison.Ordinal));
    }

    // Made anew by a script, the volume has another serial number, by which the installed Linux then knows it.
    [Fact]
    public async Task KeepsTheSerialNumberTheVolumeHasOnTheDisk()
    {
        SequenceState state = await _seeded.SeededAsync();
        _seeded.RewriteSeed(_seeded.Written(), serialNumber: 0x0BADF00D);
        LinuxHandOver handOver = _seeded.HandOver();

        handOver.Stage(Run.Session, state);
        Assert.Equal(0x0BADF00Du, TestSeed.Read(Disk).Volume.SerialNumber);

        handOver.Withdraw(Run.Session);
        Assert.Equal(0x0BADF00Du, TestSeed.Read(Disk).Volume.SerialNumber);
    }

    [Fact]
    public async Task RefusesASeedWhosePartitionAScriptRemoved()
    {
        DeploymentStepException refusal = await RefusedAsync(seeded => RewriteTable(seeded, seedFirstSector: null));

        Assert.Equal(
            "The cloud-init seed is no longer partition 3 of disk 0, where this run wrote it, so the agent cannot be put on it. " + Nothing,
            refusal.Message);
    }

    [Fact]
    public async Task RefusesASeedWhosePartitionAScriptMoved()
    {
        DeploymentStepException refusal = await RefusedAsync(
            seeded => RewriteTable(seeded, seeded.Kept.FirstSector - GptLayout.AlignmentSectors));

        Assert.StartsWith("The cloud-init seed is no longer partition 3 of disk 0, where this run wrote it", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesADiskWhosePartitionTableIsDamaged()
    {
        DeploymentStepException refusal = await RefusedAsync(seeded => seeded.Disk.Write(GptLayout.SectorSize, new byte[GptLayout.SectorSize]));

        Assert.StartsWith("The cloud-init seed is no longer partition 3 of disk 0, where this run wrote it", refusal.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidGptException>(refusal.InnerException);
    }

    [Fact]
    public async Task RefusesAPartitionThatHoldsNoFatVolumeAnyMore()
    {
        DeploymentStepException refusal = await RefusedAsync(seeded => seeded.PutInSeedsPlace(new byte[CloudInitSeed.SizeBytes]));

        Assert.Equal(
            "Partition 3 of disk 0 is no longer the cloud-init seed this run wrote, a FAT volume labelled CIDATA, so the agent cannot " +
            "be put on it: The partition holds no FAT file system. A step after the seed step may change the seed's files, but not its " +
            "file system. " + Nothing,
            refusal.Message);
    }

    // cloud-init finds the seed by its label, so a volume with another one is none.
    [Fact]
    public async Task RefusesAVolumeWithAnotherLabel()
    {
        byte[] other = new FatVolumeBuilder(CloudInitSeed.SizeBytes, "DATA", 7, DateTime.UnixEpoch) { Type = FatType.Fat16 }.Build();

        DeploymentStepException refusal = await RefusedAsync(seeded => seeded.PutInSeedsPlace(other));

        Assert.Contains("so the agent cannot be put on it: The volume is labelled DATA. A step after the seed step", refusal.Message, StringComparison.Ordinal);
    }

    // The run goes there through DDT's own vendor-data and its ddt folder, and a seed holds one of each at most.
    [Theory]
    [InlineData("vendor-data")]
    [InlineData("ddt/token")]
    [InlineData("DDT/notes.txt")]
    public async Task RefusesASeedThatBringsAFileWhereTheAgentGoes(string path)
    {
        DeploymentStepException refusal = await RefusedAsync(seeded => seeded.RewriteSeed([.. seeded.Written(), Text(path, "theirs")]));

        Assert.Equal(
            $"The cloud-init seed holds {path}, which a step after the seed step put there. The run goes on in the installed Linux " +
            "through a vendor-data file and a ddt folder of DDT's own on the seed, so the sequence cannot bring its own. " + Nothing,
            refusal.Message);
    }

    // A damaged volume can hold one name twice, which no volume built again can.
    [Fact]
    public async Task RefusesAVolumeThatCannotBeWrittenAgainWithItsFiles()
    {
        DeploymentStepException refusal = await RefusedAsync(seeded =>
        {
            byte[] volume = CloudInitSeed.Build(
                [.. seeded.Written(), Text("A.TXT", "one"), Text("B.TXT", "two")],
                seeded.Kept.SerialNumber,
                seeded.Kept.Timestamp,
                seeded.Kept.FirstSector);
            "A       TXT"u8.CopyTo(volume.AsSpan(volume.AsSpan().IndexOf("B       TXT"u8)));
            seeded.PutInSeedsPlace(volume);
        });

        Assert.StartsWith("The cloud-init seed of disk 0 cannot be written again with the files it holds now (", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("A.TXT", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesFilesThatLeaveNoRoomForTheAgent()
    {
        SequenceState state = await _seeded.SeededAsync();
        long room = CloudInitSeed.FreeBytes(_seeded.Written());
        _seeded.RewriteSeed([.. _seeded.Written(), new SeedFile("large.bin", new byte[room - (64 * 1024)])]);
        int writes = Disk.Writes.Count;

        DeploymentStepException refusal = Assert.Throws<DeploymentStepException>(() => _seeded.HandOver().Stage(Run.Session, state));

        Assert.Matches(@"^The agent and the run's files are \d+ KB too large for the cloud-init seed of 64 MB, ", refusal.Message);
        Assert.Equal(writes, Disk.Writes.Count);
        Assert.Equal("large.bin", TestSeed.Read(Disk).Names().Last());
    }
}
