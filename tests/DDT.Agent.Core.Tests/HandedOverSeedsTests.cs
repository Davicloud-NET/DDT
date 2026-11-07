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

    // Sent as a token, such bytes would be refused, and a registration with a refused token ends the machine's run.
    [Theory]
    [InlineData(20 * 1024, (byte)'t')]
    [InlineData(40, (byte)0xC3)]
    public async Task ATokenFileThatHoldsNoTokenIsSaidAndTheDiskIsStillLookedAt(int bytes, byte filled)
    {
        await _seeded.HandedOverAsync();
        byte[] content = new byte[bytes];
        content.AsSpan().Fill(filled);
        _seeded.ChangeSeed(file => Is(file, LinuxAgentFiles.SeedToken) ? file with { Content = content } : file);

        HandedOverSeed seed = Assert.Single(Find());

        Assert.Null(seed.Token);
        Assert.Equal(StepRunnerFixture.RunId, seed.RunId);
        Assert.Equal(
            bytes > 16 * 1024 || filled > 0x7E ? [HandedOverSeeds.NoTokenMessage(FakeDeploymentTools.Disk(0), bytes)] : [],
            (await WarningsAsync()).Select(line => line.Message));
    }

    [Fact]
    public async Task AFolderInTheStatesPlaceNamesNoRun()
    {
        await _seeded.HandedOverAsync();
        _seeded.ChangeSeed(file => Is(file, LinuxAgentFiles.SeedState) ? new SeedFile($"{LinuxAgentFiles.SeedState}/inner", [1, 2, 3]) : file);
        _seeded.WipeToken();

        HandedOverSeed seed = Assert.Single(Find());

        Assert.Null(seed.RunId);
        Assert.True(seed.IsTaken);
        Assert.Empty(await WarningsAsync());
    }

    // DDT writes no image to such a disk, so its table is none a hand-over made.
    [Fact]
    public async Task ADiskWhoseTableIsForSectorsOfFourKilobytesHoldsNoRunAndIsNotEvenSaid()
    {
        await _seeded.HandedOverAsync();
        byte[] header = Disk.ReadAt(GptLayout.SectorSize, GptLayout.SectorSize);
        Disk.Write(GptLayout.SectorSize, new byte[GptLayout.SectorSize]);
        Disk.Write(4096, header);

        Assert.Empty(Find());
        Assert.Empty(await WarningsAsync());
    }

    // An image written to a smaller disk than its table was made for.
    [Fact]
    public async Task ASeedPartitionPastTheDisksEndIsSaidAndHoldsNoRun()
    {
        await _seeded.HandedOverAsync();
        MemoryRawDisk shorter = new(_seeded.Kept.FirstSector * GptLayout.SectorSize);
        shorter.Write(0, Disk.ReadAt(0, GptLayout.MaxHeadBytes));
        Run.RawDisks.Disks[0] = shorter;

        Assert.Empty(Find());
        Assert.StartsWith("Disk 0 could not be looked at for a run that was handed over to the installed Linux (", Assert.Single(await WarningsAsync()).Message, StringComparison.Ordinal);
    }

    // The table allows thousands of partitions, and each one that looks like a seed is read whole before the agent registers.
    [Theory]
    [InlineData(HandedOverSeeds.MaxSeedsPerDisk, true)]
    [InlineData(HandedOverSeeds.MaxSeedsPerDisk + 1, false)]
    public async Task LooksAtNoMoreSeedsOnADiskThanAHandOverCouldHaveLeft(int position, bool looked)
    {
        await _seeded.HandedOverAsync();
        byte[] handedOver = Disk.ReadAt(_seeded.Kept.FirstSector * GptLayout.SectorSize, (int)CloudInitSeed.SizeBytes);
        byte[] plain = CloudInitSeed.Build(_seeded.Written(), 7, Run.Time.GetUtcNow().UtcDateTime, 0);
        long sectors = CloudInitSeed.SizeBytes / GptLayout.SectorSize;
        const int Seeds = HandedOverSeeds.MaxSeedsPerDisk + 2;
        MemoryRawDisk many = new(((Seeds + 1) * CloudInitSeed.SizeBytes) + (4 * GptLayout.MaxHeadBytes));
        GptLayout layout = GptLayout.Create(many.Length / GptLayout.SectorSize, Guid.NewGuid());

        for (int number = 1; number <= Seeds; number++)
        {
            long first = GptLayout.AlignmentSectors + ((number - 1) * sectors);
            layout = layout.WithPartition(GptPartitionTypes.BasicData, Guid.NewGuid(), CloudInitSeed.Label, first, first + sectors - 1);
            many.Write(first * GptLayout.SectorSize, number == position ? handedOver : plain);
        }

        many.Write(0, layout.ProtectiveMbr(default));
        many.Write(GptLayout.SectorSize, layout.PrimaryHeader());
        many.Write(layout.EntriesLba * GptLayout.SectorSize, layout.EntryArray());
        Run.RawDisks.Disks[0] = many;

        Assert.Equal(looked ? 1 : 0, Find().Count);
        Assert.Equal(HandedOverSeeds.TooManySeedsMessage(FakeDeploymentTools.Disk(0), Seeds), Assert.Single(await WarningsAsync()).Message);
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

    // Taken for empty, the registration that follows would end a run that goes on in the installed Linux.
    [Fact]
    public async Task APartitionNamedLikeASeedThatIsNoFatVolumeIsSaidAndCountsAsASeedThatNamesNoRun()
    {
        await _seeded.HandedOverAsync();
        byte[] noise = new byte[CloudInitSeed.SizeBytes];
        new Random(3).NextBytes(noise);
        _seeded.PutInSeedsPlace(noise);

        HandedOverSeed seed = Assert.Single(Find());

        Assert.Equal((true, true, null, null), (seed.IsUnread, seed.IsTaken, seed.RunId, seed.Token));
        Assert.Equal((0, _seeded.Kept.PartitionNumber, TestRawImage.EspId), (seed.Disk.Number, seed.Partition.Number, seed.SystemPartition?.PartitionId));
        AgentLogLine said = Assert.Single(await WarningsAsync());
        Assert.StartsWith("The cloud-init seed of disk 0 cannot be read (", said.Message, StringComparison.Ordinal);
        Assert.EndsWith(
            "A run that was handed over to the installed Linux may be on it, so the agent takes the seed for one that holds a run it cannot name.",
            said.Message,
            StringComparison.Ordinal);
    }

    // The folder's cluster chain is damaged, so neither the state nor the token can be looked for.
    [Fact]
    public async Task ASeedWhoseFolderCannotBeReadCountsAsASeedThatNamesNoRun()
    {
        await _seeded.HandedOverAsync();
        _seeded.DamageSeedEntry("DDT~1");

        HandedOverSeed seed = Assert.Single(Find());

        Assert.Equal((true, true, null), (seed.IsUnread, seed.IsTaken, seed.RunId));
        Assert.Equal(
            HandedOverSeeds.SeedUnreadMessage(FakeDeploymentTools.Disk(0), "A cluster chain on the partition's FAT file system is damaged."),
            Assert.Single(await WarningsAsync()).Message);
    }

    // The table names the seed, so it is there. Taken for a disk without one, the registration would end a live run.
    [Fact]
    public async Task ASeedTheDiskCannotReadIsSaidAndCountsAsASeedThatNamesNoRun()
    {
        await _seeded.HandedOverAsync();
        Disk.UnreadableFrom = (_seeded.Kept.FirstSector * GptLayout.SectorSize) + 4096;

        HandedOverSeed seed = Assert.Single(Find());

        Assert.Equal((true, true, null, null), (seed.IsUnread, seed.IsTaken, seed.RunId, seed.Token));
        Assert.Equal((0, _seeded.Kept.PartitionNumber, TestRawImage.EspId), (seed.Disk.Number, seed.Partition.Number, seed.SystemPartition?.PartitionId));
        Assert.Equal(
            HandedOverSeeds.SeedUnreadMessage(
                FakeDeploymentTools.Disk(0),
                $"The disk cannot be read at byte {(_seeded.Kept.FirstSector * GptLayout.SectorSize) + 4096} (Input/output error)."),
            Assert.Single(await WarningsAsync()).Message);
    }

    // Without its table nothing says that the disk has a seed, so it holds none, as before.
    [Fact]
    public async Task ADiskWhoseTableCannotBeReadIsSaidAndHoldsNoRun()
    {
        await _seeded.HandedOverAsync();
        Disk.UnreadableFrom = GptLayout.SectorSize;

        Assert.Empty(Find());

        Assert.Equal(
            HandedOverSeeds.UnreadMessage(FakeDeploymentTools.Disk(0), $"The disk cannot be read at byte {GptLayout.SectorSize} (Input/output error)."),
            Assert.Single(await WarningsAsync()).Message);
    }

    // The state is fine, so the seed still names its run, and the agent asks about that run.
    [Fact]
    public async Task ATokenFileThatCannotBeReadIsSaidAndCountsAsNoToken()
    {
        await _seeded.HandedOverAsync();
        _seeded.DamageSeedEntry("TOKEN~1");

        HandedOverSeed seed = Assert.Single(Find());

        Assert.Equal((false, true, StepRunnerFixture.RunId), (seed.IsUnread, seed.IsTaken, seed.RunId));
        AgentLogLine said = Assert.Single(await WarningsAsync());
        Assert.Equal(
            HandedOverSeeds.TokenUnreadMessage(FakeDeploymentTools.Disk(0), "A cluster chain on the partition's FAT file system is damaged."),
            said.Message);
        Assert.DoesNotContain(StepRunnerFixture.RunToken, said.Message, StringComparison.Ordinal);
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

    // Its run may be the one that was assigned last, and only the server knows.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASeedWithoutATokenThatNamesNoRunComesBeforeOneWhoseRunIsKnown(bool unread)
    {
        await _seeded.HandedOverAsync(s_laterRun);
        await _other.HandedOverAsync();
        AddDisk(_other.Disk);

        if (unread)
        {
            _other.DamageSeedEntry("DDT~1");
        }
        else
        {
            _other.ChangeSeed(file => Is(file, LinuxAgentFiles.SeedState) ? file with { Content = [] } : Is(file, LinuxAgentFiles.SeedToken) ? null : file);
        }

        IReadOnlyList<HandedOverSeed> found = Find();

        Assert.Equal([1, 0], found.Select(seed => seed.Disk.Number));
        Assert.Equal((null, true, unread), (found[0].RunId, found[0].IsTaken, found[0].IsUnread));
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
