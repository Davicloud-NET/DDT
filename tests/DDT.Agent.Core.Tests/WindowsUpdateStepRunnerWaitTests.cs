// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.WindowsUpdates;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// How a Windows Update step waits: for someone else's installation, for its own jobs, and after a stop.
public sealed class WindowsUpdateStepRunnerWaitTests
{
    private static readonly WindowsUpdateStep s_step = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000f2"), Name = "Windows Update" };

    private readonly WindowsUpdateRun _run = new();

    [Fact]
    public async Task WaitsWhileWindowsUpdateIsBusyWithAnotherInstallation()
    {
        _run.Updates.Busy.Enqueue(true);
        _run.Updates.Busy.Enqueue(true);

        StepResult result = await _run.Time.ThroughWaitsAsync(_run.RunAsync(s_step));

        Assert.Equal(StepResult.Done(), result);
        Assert.Equal(3, _run.Updates.Calls.Count(call => call == "busy?"));
        Assert.Contains(WindowsUpdateStepRunner.Waits, _run.Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GivesUpAfterHalfAnHourOfAnotherInstallation()
    {
        foreach (int _ in Enumerable.Range(0, 200))
        {
            _run.Updates.Busy.Enqueue(true);
        }

        Task<StepResult> running = _run.RunAsync(s_step);
        await _run.Time.AdvanceUntilAsync(WindowsUpdateStepRunner.BusyPoll, () => running.IsCompleted);

        Assert.Equal(StepResult.Failed(WindowsUpdateStepRunner.StillBusy), await running);
        Assert.DoesNotContain("search", _run.Updates.Calls);
    }

    [Fact]
    public async Task AStopEndsADownloadAtOnce()
    {
        using CancellationTokenSource stop = new();
        _run.Updates.Found.Add([WindowsUpdateStepRunnerTests.s_cumulative]);
        _run.Updates.HoldDownload = true;

        Task<StepResult> running = _run.RunAsync(s_step, cancellationToken: stop.Token);
        await stop.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.DoesNotContain(_run.Updates.Calls, call => call.StartsWith("install", StringComparison.Ordinal));
        Assert.Empty(_run.Saves);
    }

    // Windows Update cannot abort an installation, so the step returns only once it has ended, and keeps what it did.
    [Fact]
    public async Task AStopWaitsForTheInstallationAndKeepsWhatItInstalled()
    {
        using CancellationTokenSource stop = new();
        _run.Updates.Found.Add([WindowsUpdateStepRunnerTests.s_cumulative]);
        _run.Updates.HoldInstall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<StepResult> running = _run.RunAsync(s_step, cancellationToken: stop.Token);
        await _run.Updates.Installing.Task;
        await stop.CancelAsync();

        Assert.True(_run.Updates.AbortAsked);
        Assert.False(running.IsCompleted);

        _run.Updates.HoldInstall.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.True(_run.Updates.InstallEnded);
        Assert.Equal("2026-11 Cumulative Update (KB5070001)", UpdateCheckpoint.Read(Assert.Single(_run.Saves)).Installed["a"]);
    }

    [Fact]
    public async Task ADownloadThatRunsPastSixHoursFailsTheStep()
    {
        _run.Updates.Found.Add([WindowsUpdateStepRunnerTests.s_cumulative]);
        _run.Updates.HoldDownload = true;

        Task<StepResult> running = _run.RunAsync(s_step);
        await _run.Time.AdvanceUntilAsync(TimeSpan.FromHours(1), () => running.IsCompleted);

        DeploymentStepException failure = await Assert.ThrowsAsync<DeploymentStepException>(() => running);
        Assert.Equal("Windows Update's download did not end within 6 hours.", failure.Message);
    }

    [Fact]
    public async Task AnInstallationThatRunsPastSixHoursFailsTheStepAndIsAskedToAbort()
    {
        _run.Updates.Found.Add([WindowsUpdateStepRunnerTests.s_cumulative]);
        _run.Updates.HoldInstall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<StepResult> running = _run.RunAsync(s_step);
        await _run.Updates.Installing.Task;
        await _run.Time.AdvanceUntilAsync(TimeSpan.FromHours(1), () => running.IsCompleted);

        DeploymentStepException failure = await Assert.ThrowsAsync<DeploymentStepException>(() => running);
        Assert.Equal("Windows Update's installation did not end within 6 hours.", failure.Message);
        Assert.True(_run.Updates.AbortAsked);
        _run.Updates.HoldInstall.SetResult();
    }

    [Fact]
    public async Task ATurnDisposesWindowsUpdateWhenItEnds()
    {
        await _run.RunAsync(s_step);

        Assert.Equal(1, _run.Updates.Disposals);
    }

    [Fact]
    public async Task ATurnThatFailsDisposesWindowsUpdateToo()
    {
        _run.Updates.Found.Add([WindowsUpdateStepRunnerTests.s_cumulative]);
        _run.Updates.HoldDownload = true;

        Task<StepResult> running = _run.RunAsync(s_step);
        await _run.Time.AdvanceUntilAsync(TimeSpan.FromHours(1), () => running.IsCompleted);

        await Assert.ThrowsAsync<DeploymentStepException>(() => running);
        Assert.Equal(1, _run.Updates.Disposals);
    }
}
