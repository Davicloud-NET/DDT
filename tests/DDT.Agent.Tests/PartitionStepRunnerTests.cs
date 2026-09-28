// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.AccessControl;
using System.Security.Principal;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class PartitionStepRunnerTests : IDisposable
{
    private static readonly PartitionStep s_step = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a1"),
        Name = "Partition",
        SystemPartitionMegabytes = 500,
        RecoveryPartitionMegabytes = 2048,
    };

    private readonly StepRunnerFixture _run = new([s_step]);

    public void Dispose() => _run.Dispose();

    [Fact]
    public async Task PartitionsTheChosenDiskWithTheStepsSizes()
    {
        _run.Session.Disk = FakeDeploymentTools.Disk(2);

        StepResult result = await _run.Partition.RunAsync(s_step, _run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Done, result.Outcome);
        Assert.Equal(["partition 2"], _run.Tools.Calls);
        Assert.Equal((500, 2048), _run.Tools.PartitionSizes);
        Assert.Equal(_run.Tools.Volumes.Windows, _run.Session.Volumes?.Windows);
        Assert.Equal(FakeDeploymentTools.WindowsPartitionId, _run.Session.Volumes?.WindowsPartitionId);
        Assert.Equal(100, _run.Progress.Values[^1]);
    }

    [Fact]
    public async Task OutputsTheIdsThatFindThePartitionsAgain()
    {
        StepResult result = await _run.Partition.RunAsync(s_step, _run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(
            new Dictionary<string, string>
            {
                [RunVariables.SystemPartition] = FakeDeploymentTools.SystemPartitionId.ToString("D"),
                [RunVariables.WindowsPartition] = FakeDeploymentTools.WindowsPartitionId.ToString("D"),
                [RunVariables.RecoveryPartition] = FakeDeploymentTools.RecoveryPartitionId.ToString("D"),
                [RunVariables.ErasedSystemPartitions] = FakeDeploymentTools.ErasedSystemPartitionId.ToString("D"),
            },
            result.Outputs);
    }

    [Fact]
    public async Task GivesTheRunItsDirectoryOnTheWindowsVolume()
    {
        await _run.Partition.RunAsync(s_step, _run.Context(), TestContext.Current.CancellationToken);

        string directory = Path.Combine(_run.Tools.Volumes.Windows, "DDT");
        Assert.Equal(directory, _run.Session.RunDirectory);
        Assert.True(Directory.Exists(directory));
        Assert.Contains(
            await _run.SentLinesAsync(),
            line => line.Message == $"Dry run: {directory} is left open. In Windows PE only SYSTEM could open it ({SystemOnlyDirectory.Sddl}).");
    }

    [Fact]
    public async Task OutsideADryRunOnlySystemCanOpenTheRunsDirectory()
    {
        // With no state and no run token yet, the store writes nothing into the directory, which the test's account can
        // no longer write to.
        FileRunStateStore store = new(new DeploymentTokens("session", "resume"));
        PartitionStepRunner partition = new(_run.Tools, _run.Session, store, _run.Log, dryRun: false);
        string directory = Path.Combine(_run.Tools.Volumes.Windows, "DDT");

        try
        {
            await partition.RunAsync(s_step, _run.Context(), TestContext.Current.CancellationToken);

            DirectorySecurity security = new DirectoryInfo(directory).GetAccessControl(AccessControlSections.Access);
            Assert.True(security.AreAccessRulesProtected);
            FileSystemAccessRule rule = Assert.IsType<FileSystemAccessRule>(Assert.Single(security.GetAccessRules(true, true, typeof(SecurityIdentifier))));
            Assert.Equal(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), rule.IdentityReference);
            Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights);
        }
        finally
        {
            SystemOnlyDirectoryTests.Reopen(directory);
        }
    }

    [Fact]
    public async Task FromHereOnTheRunsStateAndTokenAreOnTheDisk()
    {
        SequenceState running = SequenceStates.Start(StepRunnerFixture.RunId, _run.Session.Run.Sequence) with
        {
            Steps = [new StepRunState(s_step.Id, StepState.Running, null)],
        };
        await _run.Store.SaveAsync(running, TestContext.Current.CancellationToken);

        await _run.Partition.RunAsync(s_step, _run.Context(), TestContext.Current.CancellationToken);

        RunFiles files = RunFiles.In(_run.Tools.Volumes.Windows, _run.Log);
        Assert.Equal(files.StatePath, _run.Store.Files?.StatePath);
        Assert.Equal(StepState.Running, (await files.LoadStateAsync(TestContext.Current.CancellationToken))?.Steps[0].State);
        Assert.Equal(StepRunnerFixture.RunToken, await files.LoadTokenAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PartitionsNothingWithoutAChosenDisk()
    {
        _run.Session.Disk = null;

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(
            () => _run.Partition.RunAsync(s_step, _run.Context(), TestContext.Current.CancellationToken));

        Assert.Equal("No disk was chosen for this run, so nothing was partitioned.", exception.Message);
        Assert.Empty(_run.Tools.Calls);
    }
}
