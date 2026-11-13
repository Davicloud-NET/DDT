// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.WindowsUpdates;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// The turns of a Windows Update step: search, download, install, restart, search again.
public sealed class WindowsUpdateStepRunnerTests
{
    internal static readonly AvailableUpdate s_cumulative = new("a", 1, "2026-11 Cumulative Update (KB5070001)", ["5070001"]);
    internal static readonly AvailableUpdate s_dotnet = new("b", 1, ".NET Framework update", ["5070002"]);

    private readonly WindowsUpdateRun _run = new();

    private static WindowsUpdateStep Step(Func<WindowsUpdateStep, WindowsUpdateStep>? change = null)
    {
        WindowsUpdateStep step = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000f1"), Name = "Windows Update" };

        return change is null ? step : change(step);
    }

    [Fact]
    public async Task InstallsWhatTheSearchFindsAndSearchesAgainUntilNothingIsLeft()
    {
        _run.Updates.Found.AddRange([[s_cumulative, s_dotnet], []]);

        StepResult result = await _run.RunAsync(Step());

        Assert.Equal(StepResult.Done(), result);
        Assert.Equal(["restart?", "busy?", "search", "download a,b", "install a,b", "restart?", "search"], _run.Updates.Calls);
        Assert.Equal(["Downloading 2 updates", "Installing 2 updates"], _run.Details.Values);
        Assert.Equal(100, _run.Progress.Values[^1]);
        Assert.Contains("To install: .NET Framework update (KB5070002).", _run.Log, StringComparison.Ordinal);
        Assert.Contains("Installed 2026-11 Cumulative Update (KB5070001).", _run.Log, StringComparison.Ordinal);
        Assert.Contains("Windows Update: 2 installed (2026-11 Cumulative Update (KB5070001); .NET Framework update (KB5070002)).", _run.Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchesMicrosoftUpdateUnlessTheStepLeavesOtherProductsOut()
    {
        await _run.RunAsync(Step());
        await _run.RunAsync(Step(step => step with { MicrosoftProducts = false }));

        Assert.Equal([new WindowsUpdateSearch(true), new WindowsUpdateSearch(false)], _run.Updates.Searches);
        Assert.Contains("Windows Update: nothing installed.", _run.Log, StringComparison.Ordinal);
    }

    // The checkpoint carries what was installed, so the result after the last turn names it all.
    [Fact]
    public async Task AnUpdateThatNeedsARestartRestartsTheMachineAndTheTurnAfterSearchesAgain()
    {
        _run.Updates.Found.AddRange([[s_cumulative], [s_dotnet], []]);
        _run.Updates.Installs["a"] = new UpdateOutcome("a", UpdateResult.Succeeded, RestartNeeded: true);

        StepResult first = await _run.RunAsync(Step());
        StepResult after = await _run.RunAsync(Step(), first.Progress, restarts: 1);

        Assert.Equal(StepOutcome.RestartAndContinue, first.Outcome);
        Assert.Equal(StepResult.Done(), after);
        Assert.Contains("Installed 2026-11 Cumulative Update (KB5070001), which needs a restart.", _run.Log, StringComparison.Ordinal);
        Assert.Contains(WindowsUpdateStepRunner.GoesOn, _run.Log, StringComparison.Ordinal);
        Assert.Contains("Windows Update: 2 installed (2026-11 Cumulative Update (KB5070001); .NET Framework update (KB5070002)).", _run.Log, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task TheJobOrWindowsAskingForARestartRestartsTheMachine(bool job, bool windows)
    {
        _run.Updates.Found.Add([s_cumulative]);
        _run.Updates.InstallationNeedsRestart = job;
        _run.Updates.PendingAfterInstall = windows;

        StepResult result = await _run.RunAsync(Step());

        Assert.Equal(StepOutcome.RestartAndContinue, result.Outcome);
    }

    [Fact]
    public async Task ARestartPendingBeforeAnythingIsInstalledComesFirst()
    {
        _run.Updates.RestartPending = true;

        StepResult result = await _run.RunAsync(Step());

        Assert.Equal(StepResult.RestartAndContinue("""{"installed":{},"failed":{}}"""), result);
        Assert.Equal(["restart?"], _run.Updates.Calls);
        Assert.Contains(WindowsUpdateStepRunner.PendingFirst, _run.Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcceptsALicenceNotYetAccepted()
    {
        _run.Updates.Found.Add([s_cumulative with { EulaAccepted = false }]);

        await _run.RunAsync(Step());

        Assert.Equal(["restart?", "busy?", "search", "eula a", "download a", "install a", "restart?", "search"], _run.Updates.Calls);
        Assert.Contains("Accepting the licence terms of 2026-11 Cumulative Update (KB5070001).", _run.Log, StringComparison.Ordinal);
    }

    // The second failure leaves it out, and the step fails at the end with the HRESULT.
    [Fact]
    public async Task AFailedUpdateIsTriedOnceMoreThenLeftOutAndFailsTheStep()
    {
        _run.Updates.Found.AddRange([[s_cumulative, s_dotnet], [s_cumulative], [s_cumulative]]);
        _run.Updates.Installs["a"] = new UpdateOutcome("a", UpdateResult.Failed, unchecked((int)0x80240022));

        StepResult result = await _run.RunAsync(Step());

        Assert.Equal(StepResult.Failed("Windows Update could not install: 2026-11 Cumulative Update (KB5070001) (0x80240022)."), result);
        Assert.Equal(["install a,b", "install a"], _run.Updates.Calls.Where(call => call.StartsWith("install", StringComparison.Ordinal)));
        Assert.Contains("The installation of 2026-11 Cumulative Update (KB5070001) failed (0x80240022). It is tried once more.", _run.Log, StringComparison.Ordinal);
        Assert.Contains("The installation of 2026-11 Cumulative Update (KB5070001) failed again (0x80240022). It is left out.", _run.Log, StringComparison.Ordinal);
        Assert.Contains("Windows Update: 1 installed (.NET Framework update (KB5070002)).", _run.Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedDownloadCountsAsATryAndIsNotInstalled()
    {
        _run.Updates.Found.AddRange([[s_cumulative, s_dotnet], []]);
        _run.Updates.Downloads["a"] = new UpdateOutcome("a", UpdateResult.Failed, unchecked((int)0x80244022));

        StepResult result = await _run.RunAsync(Step());

        Assert.Equal(StepResult.Done(), result);
        Assert.Contains("install b", _run.Updates.Calls);
        Assert.Contains("The download of 2026-11 Cumulative Update (KB5070001) failed (0x80244022). It is tried once more.", _run.Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdatesThatKeepComingRestartTheMachineAfterFiveSearches()
    {
        _run.Updates.Found.AddRange(Enumerable.Repeat<IReadOnlyList<AvailableUpdate>>([s_cumulative], 6));

        StepResult result = await _run.RunAsync(Step());

        Assert.Equal(StepOutcome.RestartAndContinue, result.Outcome);
        Assert.Equal(WindowsUpdateStepRunner.MaxPasses, _run.Updates.Calls.Count(call => call == "search"));
        Assert.Contains(WindowsUpdateStepRunner.StillOffered(5), _run.Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheStepsOwnRestartLimitEndsItWithTheUpdatesStillOpen()
    {
        _run.Updates.Found.Add([s_cumulative, s_dotnet]);
        _run.Updates.InstallationNeedsRestart = true;

        StepResult result = await _run.RunAsync(Step(step => step with { AllowedRestarts = 2 }), restarts: 2);

        Assert.Equal(
            StepResult.Failed("The step reached its limit of 2 restarts with updates still open: 2026-11 Cumulative Update (KB5070001); .NET Framework update (KB5070002)."),
            result);
    }

    [Fact]
    public async Task ARestartPendingAtTheLimitFailsTheStep()
    {
        _run.Updates.RestartPending = true;

        StepResult result = await _run.RunAsync(Step(), restarts: WindowsUpdateStep.DefaultRestarts);

        Assert.Equal(StepResult.Failed("The step reached its limit of 8 restarts, and Windows still needs a restart."), result);
    }

    [Fact]
    public async Task ACheckpointThatCannotBeReadCountsAsNone()
    {
        StepResult result = await _run.RunAsync(Step(), "not json");

        Assert.Equal(StepResult.Done(), result);
    }
}
