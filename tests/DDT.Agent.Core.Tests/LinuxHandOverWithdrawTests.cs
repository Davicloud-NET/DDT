// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// Taking a run off its seed again, and a disk the hand-over cannot have. Whatever fails while the run is taken off is
// said and never thrown, because the run's end still has to restore the boot order and tell the server.
public sealed class LinuxHandOverWithdrawTests : IDisposable
{
    private const string CouldNot = "The agent and the run's token could not be taken off the cloud-init seed of disk 0 again";

    private readonly SeededRun _seeded = new();

    public void Dispose() => _seeded.Dispose();

    private StepRunnerFixture Run => _seeded.Run;

    private MemoryRawDisk Disk => _seeded.Disk;

    private async Task<AgentLogLine> LastLineAsync() => (await Run.SentLinesAsync())[^1];

    [Fact]
    public async Task AWriteThatStopsHalfWaySaysSoAndIsTakenBack()
    {
        SequenceState state = await _seeded.SeededAsync();
        TestSeed before = TestSeed.Read(Disk);
        LinuxHandOver handOver = _seeded.HandOver();
        Disk.FailNextWriteAfter = 4 * 1024 * 1024;

        DeploymentStepException failure = Assert.Throws<DeploymentStepException>(() => handOver.Stage(Run.Session, state));

        Assert.Equal(
            "The agent could not be written to the cloud-init seed of disk 0, so the run cannot go on in the installed Linux: " +
            "The device is not ready.",
            failure.Message);
        Assert.True(_seeded.Kept.HoldsRun);
        Assert.True(TestSeed.Read(Disk).Holds(StepRunnerFixture.RunToken));

        Assert.True(handOver.Withdraw(Run.Session, HandOverEnd.Failed));

        TestSeed after = TestSeed.Read(Disk);
        Assert.Equal(before.Bytes, after.Bytes);
        Assert.Equal(["meta-data", "user-data", "network-config"], after.Names());
        Assert.False(after.Holds(StepRunnerFixture.RunToken));
        Assert.False(_seeded.Kept.HoldsRun);
        Assert.Equal(
            "The agent and the run's token are off the cloud-init seed again, which is as it was before the hand-over.",
            (await LastLineAsync()).Message);
    }

    // The disk no longer takes a write. The warning says what the token that stays is still good for, and never the token.
    [Theory]
    [InlineData(
        HandOverEnd.Failed,
        "The server takes that token only for this run, which has failed. If the machine starts from this disk, the agent there " +
        "finds no run to go on with and removes itself.")]
    [InlineData(
        HandOverEnd.Stopped,
        "The server takes that token for as long as it runs this run. If the machine starts from this disk before the server has " +
        "ended the run, the installed Linux goes on with it. After that the agent there finds no run and removes itself.")]
    [InlineData(
        HandOverEnd.TokenRejected,
        "The server takes that token only while it runs this run, which it has most likely ended. If the machine starts from this " +
        "disk, the agent there removes itself, or goes on with the run should the server still run it.")]
    public async Task WarnsWhatATokenThatCannotBeTakenOffTheSeedStillMeans(HandOverEnd end, string means)
    {
        SequenceState state = await _seeded.SeededAsync();
        LinuxHandOver handOver = _seeded.HandOver();
        handOver.Stage(Run.Session, state);
        Disk.FailNextWriteAfter = 0;

        Assert.True(handOver.Withdraw(Run.Session, end));

        AgentLogLine last = await LastLineAsync();
        Assert.Equal((AgentLogLevel.Warning, $"{CouldNot} (The device is not ready.). {means}"), (last.Level, last.Message));
        Assert.True(_seeded.Kept.HoldsRun);
        Assert.Equal(StepRunnerFixture.RunToken, TestSeed.Read(Disk).Text(LinuxAgentFiles.SeedToken));
        Assert.DoesNotContain(StepRunnerFixture.RunToken, last.Message, StringComparison.Ordinal);
    }

    // None of these is a failure of the disk's device, which is all a caller could expect of a write.
    [Fact]
    public async Task ThrowsNothingWhateverFailsWhileTheRunIsTakenOff()
    {
        SequenceState state = await _seeded.SeededAsync();
        LinuxHandOver handOver = _seeded.HandOver();
        handOver.Stage(Run.Session, state);
        WrittenSeed staged = _seeded.Kept;

        // Beyond the disk's end, which the disk refuses as an argument.
        Run.Session.Seed = staged with { FirstSector = Disk.Length / 512 };
        Assert.True(handOver.Withdraw(Run.Session, HandOverEnd.Failed));
        Assert.StartsWith($"{CouldNot} (", (await LastLineAsync()).Message, StringComparison.Ordinal);

        // More than the volume holds, which the builder refuses as an operation.
        Run.Session.Seed = staged with { Found = new SeedVolume([new SeedFile("large.bin", new byte[CloudInitSeed.SizeBytes])], 1) };
        Assert.True(handOver.Withdraw(Run.Session, HandOverEnd.Failed));
        Assert.StartsWith($"{CouldNot} (The files do not fit on the volume.)", (await LastLineAsync()).Message, StringComparison.Ordinal);

        // A disk that cannot be opened any more.
        Run.Session.Seed = staged;
        BusyDisks busy = new(Run.RawDisks) { Busy = true };
        Assert.True(_seeded.HandOver(disks: busy).Withdraw(Run.Session, HandOverEnd.Stopped));
        Assert.StartsWith($"{CouldNot} ({BusyDisks.Message}). The server takes that token for as long as", (await LastLineAsync()).Message, StringComparison.Ordinal);
        Assert.True(TestSeed.Read(Disk).Holds(StepRunnerFixture.RunToken));
    }

    // The stop comes first: neither the run nor the boot order is looked at, so the test hands over none.
    [Fact]
    public async Task ARunThatIsStoppedAlreadyIsNotPutOnTheSeed()
    {
        await _seeded.SeededAsync();
        int writes = Disk.Writes.Count;
        IHandOver handOver = _seeded.HandOver();

        await Assert.ThrowsAsync<OperationCanceledException>(() => handOver.HandOverAsync(null!, null!, new CancellationToken(canceled: true)));

        Assert.Equal(writes, Disk.Writes.Count);
        Assert.False(_seeded.Kept.HoldsRun);
        Assert.False(TestSeed.Read(Disk).Holds(StepRunnerFixture.RunToken));
    }

    [Fact]
    public async Task ADiskThatCannotBeOpenedFailsTheHandOverBeforeAnythingIsWritten()
    {
        SequenceState state = await _seeded.SeededAsync();
        int writes = Disk.Writes.Count;
        BusyDisks busy = new(Run.RawDisks) { Busy = true };

        DeploymentStepException failure = Assert.Throws<DeploymentStepException>(() => _seeded.HandOver(disks: busy).Stage(Run.Session, state));

        Assert.Equal(BusyDisks.Message, failure.Message);
        Assert.Equal(writes, Disk.Writes.Count);
        Assert.False(_seeded.Kept.HoldsRun);
    }

    // A script may leave a partition of the disk mounted. The boot environment unmounts it for the restart, and the
    // hand-over needs the disk alone before that.
    [Fact]
    public async Task LetsGoOfTheDiskBeforeItOpensIt()
    {
        SequenceState state = await _seeded.SeededAsync();
        BusyDisks busy = new(Run.RawDisks) { Busy = true };
        LinuxHandOver handOver = new(busy, _seeded.AgentPath, TestAgents.Configuration, Run.Log) { LetGo = () => busy.Busy = false };

        handOver.Stage(Run.Session, state);

        Assert.Equal(StepRunnerFixture.RunToken, TestSeed.Read(Disk).Text(LinuxAgentFiles.SeedToken));
        Assert.Equal(1, busy.Opened);
    }
}
