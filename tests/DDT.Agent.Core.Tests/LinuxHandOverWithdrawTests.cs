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

// Taking a run off its seed again, as after a hand-over that did not end well.
public sealed class LinuxHandOverWithdrawTests : IDisposable
{
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

        handOver.Withdraw(Run.Session);

        TestSeed after = TestSeed.Read(Disk);
        Assert.Equal(before.Bytes, after.Bytes);
        Assert.Equal(["meta-data", "user-data", "network-config"], after.Names());
        Assert.False(after.Holds(StepRunnerFixture.RunToken));
        Assert.False(_seeded.Kept.HoldsRun);
        Assert.Equal(
            "The agent and the run's token are off the cloud-init seed again, which is as it was before the hand-over.",
            (await LastLineAsync()).Message);
    }

    // The disk no longer takes a write. The warning says where the token still is, and never the token.
    [Fact]
    public async Task WarnsWhenTheTokenCannotBeTakenOffTheSeedAgain()
    {
        SequenceState state = await _seeded.SeededAsync();
        LinuxHandOver handOver = _seeded.HandOver();
        handOver.Stage(Run.Session, state);
        Disk.FailNextWriteAfter = 0;

        handOver.Withdraw(Run.Session);

        AgentLogLine last = await LastLineAsync();
        Assert.Equal(
            (AgentLogLevel.Warning,
                "The agent and the run's token could not be taken off the cloud-init seed of disk 0 again (The device is not ready.). " +
                "The server takes that token only for this run."),
            (last.Level, last.Message));
        Assert.True(_seeded.Kept.HoldsRun);
        Assert.Equal(StepRunnerFixture.RunToken, TestSeed.Read(Disk).Text(LinuxAgentFiles.SeedToken));
    }
}
