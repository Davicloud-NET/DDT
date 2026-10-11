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
using Xunit;

namespace DDT.Agent.Tests;

// The look at the disks for a run that was handed over to the installed Linux, as a start of the boot environment
// takes it: what each seed says of its run, a disk that cannot be read, and the run taken off its seed again.
public sealed class HandedOverSeedsTests : IDisposable
{
    private static readonly Guid s_laterRun = Guid.Parse("0199a4b2-0000-7000-8000-0000000000f2");

    private readonly SeededRun _seeded = new();
    private readonly SeededRun _other = new();

    public void Dispose()
    {
        _seeded.Dispose();
        _other.Dispose();
    }

    private StepRunnerFixture Run => _seeded.Run;

    private MemoryRawDisk Disk => _seeded.Disk;

    private HandedOverSeeds Seeds(IRawDisks? disks = null) => new(disks ?? Run.RawDisks, Run.Log, Run.Time);

    private IReadOnlyList<HandedOverSeed> Find(IRawDisks? disks = null) => Seeds(disks).Find(Run.Tools.Disks);

    private async Task<List<AgentLogLine>> WarningsAsync() => [.. (await Run.SentLinesAsync()).Where(line => line.Level == AgentLogLevel.Warning)];

    // A second disk of the machine, with what another run left on it.
    private void AddDisk(MemoryRawDisk disk)
    {
        Run.Tools.Disks.Add(FakeDeploymentTools.Disk(1));
        Run.RawDisks.Disks[1] = disk;
    }

    private static bool Is(SeedFile file, string path) => SeedVolume.SamePath(file.Path, path);

    [Fact]
    public async Task ASeedARunOnlyWroteHoldsNoRun()
    {
        await _seeded.SeededAsync();

        Assert.Empty(Find());
        Assert.Empty(await WarningsAsync());
    }

    [Fact]
    public async Task ARunThatWasHandedOverAndNotTakenOverStillHasItsTokenOnTheSeed()
    {
        await _seeded.HandedOverAsync();

        HandedOverSeed seed = Assert.Single(Find());

        Assert.False(seed.IsTaken);
        Assert.Equal((StepRunnerFixture.RunToken, StepRunnerFixture.RunId), (seed.Token, seed.RunId));
        Assert.Equal((0, _seeded.Kept.PartitionNumber, _seeded.Kept.FirstSector), (seed.Disk.Number, seed.Partition.Number, seed.Partition.FirstLba));
        Assert.Equal(new EspPartition(TestRawImage.EspNumber, TestRawImage.EspFirst, TestRawImage.EspSectors, TestRawImage.EspId), seed.SystemPartition);
        Assert.Empty(await WarningsAsync());
    }

    // The agent's script wipes the token once the unit is installed, and only then.
    [Fact]
    public async Task ARunTheInstalledLinuxTookOverLeavesItsStateWithoutTheToken()
    {
        await _seeded.HandedOverAsync();
        _seeded.WipeToken();

        HandedOverSeed seed = Assert.Single(Find());

        Assert.True(seed.IsTaken);
        Assert.Null(seed.Token);
        Assert.Equal(StepRunnerFixture.RunId, seed.RunId);
    }

    // The script overwrites the token with zeros and then deletes it. Power may be lost between the two.
    [Theory]
    [InlineData("\0\0\0\0\0\0\0\0\0\0\0")]
    [InlineData("")]
    [InlineData(" \r\n")]
    public async Task ATokenThatWasOverwrittenButNotDeletedCountsAsWiped(string left)
    {
        await _seeded.HandedOverAsync();
        _seeded.ChangeSeed(file => Is(file, LinuxAgentFiles.SeedToken) ? file with { Content = Encoding.ASCII.GetBytes(left) } : file);

        Assert.True(Assert.Single(Find()).IsTaken);
    }

    // Without its state the installed Linux cannot go on either, and the token still speaks for the run.
    [Fact]
    public async Task ATokenWithoutAStateIsARunThatWasNotTakenOver()
    {
        await _seeded.HandedOverAsync();
        _seeded.ChangeSeed(file => Is(file, LinuxAgentFiles.SeedState) ? null : file);

        HandedOverSeed seed = Assert.Single(Find());

        Assert.Equal((false, StepRunnerFixture.RunToken), (seed.IsTaken, seed.Token));
        Assert.Null(seed.RunId);
    }

    [Fact]
    public async Task AStateThatCannotBeReadNamesNoRunAndStillSaysWhetherItWasTakenOver()
    {
        await _seeded.HandedOverAsync();
        _seeded.ChangeSeed(file => Is(file, LinuxAgentFiles.SeedState) ? file with { Content = "{ not json"u8.ToArray() } : file);
        HandedOverSeed waiting = Assert.Single(Find());
        Assert.False(waiting.IsTaken);
        Assert.Null(waiting.RunId);

        _seeded.WipeToken();
        HandedOverSeed taken = Assert.Single(Find());
        Assert.True(taken.IsTaken);
        Assert.Null(taken.RunId);
    }

    // What the hand-over left beside the state and the token says nothing of a run.
    [Fact]
    public async Task ASeedWithNeitherStateNorTokenHoldsNoRun()
    {
        await _seeded.HandedOverAsync();
        _seeded.ChangeSeed(file => Is(file, LinuxAgentFiles.SeedState) || Is(file, LinuxAgentFiles.SeedToken) ? null : file);

        Assert.Empty(Find());
        Assert.Equal(["ddt-agent", "agent.json"], TestSeed.Read(Disk).Names(LinuxAgentFiles.SeedFolder));
    }

    // A new disk, or one of a system DDT did not write: most machines have such a disk, so nothing is said of it.
    [Fact]
    public async Task ADiskWithoutAPartitionTableHoldsNoRunAndIsNotEvenSaid()
    {
        await _seeded.HandedOverAsync();
        AddDisk(new MemoryRawDisk(FakeDeploymentTools.Disk(1).SizeBytes));

        Assert.Equal(0, Assert.Single(Find()).Disk.Number);
        Assert.Empty(await WarningsAsync());
    }

    [Fact]
    public async Task ADamagedPartitionTableIsSaidOnceAndTheOtherDisksAreStillLookedAt()
    {
        await _seeded.HandedOverAsync();
        await _other.HandedOverAsync();
        AddDisk(_other.Disk);
        byte[] entries = _other.Disk.ReadAt(2 * GptLayout.SectorSize, GptLayout.SectorSize);
        entries[0] ^= 0xFF;
        _other.Disk.Write(2 * GptLayout.SectorSize, entries);

        Assert.Equal(0, Assert.Single(Find()).Disk.Number);

        AgentLogLine said = Assert.Single(await WarningsAsync());
        Assert.StartsWith("Disk 1 could not be looked at for a run that was handed over to the installed Linux (", said.Message, StringComparison.Ordinal);
        Assert.EndsWith("The agent goes on as if it held none.", said.Message, StringComparison.Ordinal);
    }

    // As Linux refuses a disk that a RAID or a volume group holds.
    [Fact]
    public async Task ADiskThatCannotBeOpenedIsSaidWithTheReason()
    {
        await _seeded.HandedOverAsync();

        Assert.Empty(Find(new BusyDisks(Run.RawDisks) { Busy = true }));

        Assert.Equal(HandedOverSeeds.UnreadMessage(FakeDeploymentTools.Disk(0), BusyDisks.Message), Assert.Single(await WarningsAsync()).Message);
    }

    [Fact]
    public async Task APartitionNamedLikeASeedThatIsNoFatVolumeIsSaid()
    {
        await _seeded.HandedOverAsync();
        byte[] noise = new byte[CloudInitSeed.SizeBytes];
        new Random(3).NextBytes(noise);
        _seeded.PutInSeedsPlace(noise);

        Assert.Empty(Find());
        Assert.StartsWith("Disk 0 could not be looked at for a run that was handed over to the installed Linux (", Assert.Single(await WarningsAsync()).Message, StringComparison.Ordinal);
    }

    // A machine has one run at a time, so only the run that was assigned last can be one the server still runs.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheSeedWhoseRunWasAssignedLastComesFirst(bool laterOnSecondDisk)
    {
        await _seeded.HandedOverAsync(laterOnSecondDisk ? null : s_laterRun);
        await _other.HandedOverAsync(laterOnSecondDisk ? s_laterRun : null);
        _other.WipeToken();
        AddDisk(_other.Disk);

        IReadOnlyList<HandedOverSeed> found = Find();

        Assert.Equal(laterOnSecondDisk ? (int[])[1, 0] : [0, 1], found.Select(seed => seed.Disk.Number));
        Assert.Equal(s_laterRun, found[0].RunId);
        Assert.Equal(laterOnSecondDisk, found[0].IsTaken);
    }

    [Fact]
    public async Task ASeedWhoseRunIsNotKnownComesAfterOneWhoseRunIs()
    {
        await _seeded.HandedOverAsync();
        _seeded.ChangeSeed(file => Is(file, LinuxAgentFiles.SeedState) ? null : file);
        await _other.HandedOverAsync();
        AddDisk(_other.Disk);

        Assert.Equal([1, 0], Find().Select(seed => seed.Disk.Number));
    }

    [Fact]
    public async Task TakesTheRunOffTheSeedAndLeavesNoByteOfItsToken()
    {
        SequenceState state = await _seeded.SeededAsync();
        TestSeed written = TestSeed.Read(Disk);
        _seeded.HandOver().Stage(Run.Session, state);
        int flushes = Disk.Flushes;

        Assert.True(Seeds().TakeOff(Assert.Single(Find())));

        TestSeed after = TestSeed.Read(Disk);
        Assert.Equal(["meta-data", "user-data", "network-config"], after.Names());
        Assert.All(after.Names(), name => Assert.Equal(written.File(name), after.File(name)));
        Assert.Equal((written.Volume.Label, written.Volume.SerialNumber, written.Partition), (after.Volume.Label, after.Volume.SerialNumber, after.Partition));
        Assert.False(after.Holds(StepRunnerFixture.RunToken));
        Assert.Equal(flushes + 1, Disk.Flushes);
        Assert.Empty(Find());
        Assert.Empty(await WarningsAsync());
    }

    // The state stays readable in the partition after the script deleted only the token, so it goes the same way.
    [Fact]
    public async Task TakesARunTheInstalledLinuxTookOverOffTheSeedToo()
    {
        await _seeded.HandedOverAsync();
        _seeded.WipeToken();

        Assert.True(Seeds().TakeOff(Assert.Single(Find())));

        Assert.Equal(["meta-data", "user-data", "network-config"], TestSeed.Read(Disk).Names());
        Assert.Empty(Find());
    }

    // The hand-over refuses a seed that brings its own, so one found later is the administrator's.
    [Fact]
    public async Task LeavesAVendorDataThatIsNotDdtsScriptOnTheSeed()
    {
        await _seeded.HandedOverAsync();
        byte[] own = "#cloud-config\npackages: [htop]\n"u8.ToArray();
        _seeded.ChangeSeed(file => Is(file, CloudInitSeed.VendorData) ? file with { Content = own } : file);

        Assert.True(Seeds().TakeOff(Assert.Single(Find())));

        TestSeed after = TestSeed.Read(Disk);
        Assert.Equal(["meta-data", "user-data", "network-config", "vendor-data"], after.Names());
        Assert.Equal(own, after.File(CloudInitSeed.VendorData));
    }

    [Fact]
    public async Task ASeedThatCannotBeWrittenAgainStaysAndIsSaidWithoutTheToken()
    {
        await _seeded.HandedOverAsync();
        HandedOverSeed seed = Assert.Single(Find());
        Disk.FailNextWriteAfter = 0;

        Assert.False(Seeds().TakeOff(seed));

        AgentLogLine said = Assert.Single(await WarningsAsync());
        Assert.Equal(HandedOverSeeds.NotRemovedMessage(FakeDeploymentTools.Disk(0), "The device is not ready."), said.Message);
        Assert.DoesNotContain(StepRunnerFixture.RunToken, said.Message, StringComparison.Ordinal);
        Assert.Equal(StepRunnerFixture.RunToken, TestSeed.Read(Disk).Text(LinuxAgentFiles.SeedToken));
    }

    // The agent still has to go on as after any start, whatever the disk does.
    [Fact]
    public async Task ThrowsNothingWhateverFailsWhileTheRunIsTakenOff()
    {
        await _seeded.HandedOverAsync();
        HandedOverSeed seed = Assert.Single(Find());

        Assert.False(Seeds(new BusyDisks(Run.RawDisks) { Busy = true }).TakeOff(seed));
        Assert.False(Seeds().TakeOff(new HandedOverSeed(seed.Disk, seed.Partition with { FirstLba = Disk.Length / GptLayout.SectorSize })));

        Assert.Equal(2, (await WarningsAsync()).Count);
        Assert.Equal(StepRunnerFixture.RunToken, TestSeed.Read(Disk).Text(LinuxAgentFiles.SeedToken));
    }
}
