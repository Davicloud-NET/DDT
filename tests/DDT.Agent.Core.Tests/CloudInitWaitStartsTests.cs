// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.LinuxPhase;
using Xunit;

namespace DDT.Agent.Tests;

// The wait for cloud-init across starts of Linux and of the agent: which start is the first, what a later start
// still waits for, and where the wait gives up because cloud-init does not run.
public sealed class CloudInitWaitStartsTests : IDisposable
{
    private const string PowerState = """{ "power_state": { "delay": "now", "mode": "reboot" } }""";

    private const string Spent =
        "INFO  The user-data's restart (power_state: reboot) came at an earlier start of this Linux, so the run does not wait for it again.";

    private readonly CloudInitRoot _linux = new();

    public void Dispose() => _linux.Dispose();

    // cloud-init acts on power_state at its first start alone and keeps it in its configuration for good. The restart
    // can come before the agent has seen that cloud-init finished, so the agent tells the starts apart by their ids.
    [Fact]
    public void DoesNotWaitAgainForAPowerStateThatRestartedTheMachineAtAnEarlierStart()
    {
        _linux.Start(CloudInitRoot.StartA);
        Assert.Equal(CloudInitWait.StillRunning, _linux.Wait().Pending());

        // The agent is started again at the same start of Linux, as after an error: the restart is still to come.
        _linux.Put(CloudInitFiles.ResultPath, CloudInitRoot.Clean);
        _linux.Put(CloudInitFiles.ConfigurationPath, PowerState);
        Assert.Equal(CloudInitWait.RestartComes, _linux.Wait().Pending());

        // Linux has restarted. cloud-init finishes again, and its configuration still holds the power_state.
        _linux.Start(CloudInitRoot.StartB);
        _linux.Put(CloudInitFiles.ResultPath, CloudInitRoot.Clean);
        _linux.ForgetLines();
        CloudInitWait later = _linux.Wait();

        Assert.Equal(CloudInitWait.Settling, later.Pending());
        _linux.Time.Advance(CloudInitWait.Settle);
        Assert.Null(later.Pending());
        Assert.Equal(["INFO  cloud-init has finished at this start.", $"INFO  {CloudInitReport.OutputStays}", Spent], _linux.Lines());
    }

    // On Ubuntu the restart stops the unit 0.4 s after its start: the agent has not reached the server by then, let
    // alone asked what cloud-init does. All its first start did is note which start of Linux it was.
    [Fact]
    public void KnowsItsFirstStartAlsoWhenThatStartNeverAskedAfterCloudInit()
    {
        _linux.Start(CloudInitRoot.StartA);
        _linux.Put(CloudInitFiles.ConfigurationPath, PowerState);
        _linux.FirstStart().Take();
        Assert.Equal(CloudInitRoot.StartA, File.ReadAllText(_linux.Note));

        _linux.Start(CloudInitRoot.StartB);
        _linux.Put(CloudInitFiles.ResultPath, CloudInitRoot.Clean);
        _linux.FirstStart().Take();
        CloudInitWait later = _linux.Wait();

        Assert.Equal(CloudInitWait.Settling, later.Pending());
        _linux.Time.Advance(CloudInitWait.Settle);
        Assert.Null(later.Pending());
        Assert.Contains(Spent, _linux.Lines());
        Assert.DoesNotContain(_linux.Lines(), line => line.StartsWith("WARN", StringComparison.Ordinal));
        Assert.Equal(CloudInitRoot.StartA, File.ReadAllText(_linux.Note));
    }

    // Someone ran shutdown -r at the later start. That is announced at this start, whatever the first start saw.
    [Fact]
    public void WaitsForAShutdownSomeoneScheduledAtALaterStart()
    {
        _linux.Start(CloudInitRoot.StartA);
        _linux.FirstStart().Take();
        _linux.Start(CloudInitRoot.StartB);
        _linux.Put(CloudInitFiles.ResultPath, CloudInitRoot.Clean);
        _linux.Put(CloudInitFiles.ConfigurationPath, PowerState);
        _linux.Put(CloudInitFiles.ScheduledShutdownPath, CloudInitRoot.Fixture("debian-13-shutdown-scheduled", "scheduled"));
        CloudInitWait later = _linux.Wait();

        Assert.Equal(CloudInitWait.RestartComes, later.Pending());
        Assert.Contains(
            _linux.Lines(),
            line => line.StartsWith("INFO  The user-data restarts or powers off this machine (a scheduled reboot), so the run waits", StringComparison.Ordinal));
    }

    // User-data that disables cloud-init's units and restarts, a common way to run it once. The generator still says
    // enabled at the next start, and no unit writes a status.
    [Fact]
    public void GoesOnOnceCloudInitHasShownNoSignOfRunningTwoMinutesAfterTheAgentStarted()
    {
        _linux.Start(CloudInitRoot.StartA);
        CloudInitWait wait = _linux.Wait();

        Assert.Equal(CloudInitWait.StillRunning, wait.Pending());
        _linux.Time.Advance(CloudInitWait.StartLimit - TimeSpan.FromSeconds(1));
        Assert.Equal(CloudInitWait.StillRunning, wait.Pending());
        Assert.False(File.Exists(_linux.Marker));

        _linux.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(wait.Pending());

        Assert.Equal(
            [
                "INFO  cloud-init is enabled but has written no status 2 min 0 s after the agent started, so its units do not run at this " +
                    "start of Linux. The run goes on without it.",
            ],
            _linux.Lines());
        Assert.True(File.Exists(_linux.Marker));
    }

    // The server was away for longer than the limit, so the agent asks after cloud-init for the first time past it.
    [Fact]
    public void CountsTheTwoMinutesFromTheAgentsStartNotFromItsFirstQuestion()
    {
        _linux.Start(CloudInitRoot.StartA);
        CloudInitWait wait = _linux.Wait();
        _linux.Time.Advance(TimeSpan.FromMinutes(10));

        Assert.Null(wait.Pending());
        Assert.Equal(CloudInitWait.NotRunning(TimeSpan.FromMinutes(10)), _linux.Lines()[0][6..]);
    }

    // After a restart the run asked for, cloud-init runs what it runs at every start. The run's own wait is over, so
    // there is no settle time and no look at the user-data: only this start's result, and not for long.
    [Fact]
    public void AtALaterStartWaitsOnlyForThatStartsResult()
    {
        _linux.Start(CloudInitRoot.StartA);
        _linux.Put(CloudInitFiles.ResultPath, CloudInitRoot.Clean);
        CloudInitWait first = _linux.Wait();
        Assert.Equal(CloudInitWait.Settling, first.Pending());
        _linux.Time.Advance(CloudInitWait.Settle);
        Assert.Null(first.Pending());

        _linux.Start(CloudInitRoot.StartB);
        _linux.Put(CloudInitFiles.ConfigurationPath, PowerState);
        _linux.ForgetLines();
        CloudInitWait later = _linux.Wait();

        Assert.Equal(CloudInitWait.RunsAgain, later.Pending());
        _linux.Time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(CloudInitWait.RunsAgain, later.Pending());

        _linux.Put(CloudInitFiles.ResultPath, CloudInitRoot.Clean);
        Assert.Null(later.Pending());
        Assert.Null(later.Pending());
        Assert.Null(first.Pending());
        Assert.Empty(_linux.Lines());
    }

    [Fact]
    public void AtALaterStartGoesOnBesideACloudInitThatHasNotFinishedInTwoMinutes()
    {
        _linux.Start(CloudInitRoot.StartA);
        _linux.WaitWasOver();
        _linux.Put(CloudInitFiles.StatusPath, """{ "v1": { "stage": "modules-final" } }""");
        CloudInitWait later = _linux.Wait();

        _linux.Time.Advance(CloudInitWait.StartLimit - TimeSpan.FromSeconds(1));
        Assert.Equal(CloudInitWait.RunsAgain, later.Pending());
        _linux.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(later.Pending());

        Assert.Equal(
            ["INFO  cloud-init has not finished 2 min 0 s after the agent started at this later start of Linux. The run goes on beside what it still does."],
            _linux.Lines());
    }

    // The same start of Linux, and the agent starts again, as after an error. cloud-init has long finished.
    [Fact]
    public void AnAgentStartedAgainAtTheSameStartOfLinuxDoesNotWaitAgain()
    {
        _linux.Start(CloudInitRoot.StartA);
        _linux.Put(CloudInitFiles.ResultPath, CloudInitRoot.Clean);
        CloudInitWait first = _linux.Wait();
        first.Pending();
        _linux.Time.Advance(CloudInitWait.Settle);
        Assert.Null(first.Pending());
        _linux.ForgetLines();

        Assert.Null(_linux.Wait().Pending());
        Assert.Empty(_linux.Lines());
    }

    // A full disk, or a folder where the file should be. Neither may end the wait or the agent.
    [Fact]
    public void ANoteAndAMarkerThatCannotBeWrittenAreSaidAndTheRunGoesOn()
    {
        _linux.Start(CloudInitRoot.StartA);
        _linux.Put(CloudInitFiles.ResultPath, CloudInitRoot.Clean);
        Directory.CreateDirectory(_linux.Note);
        Directory.CreateDirectory(_linux.Marker);
        CloudInitWait wait = _linux.Wait();

        Assert.Equal(CloudInitWait.Settling, wait.Pending());
        _linux.Time.Advance(CloudInitWait.Settle);
        Assert.Null(wait.Pending());
        Assert.Null(wait.Pending());

        string[] warnings = [.. _linux.Lines().Where(line => line.StartsWith("WARN", StringComparison.Ordinal))];
        Assert.Equal(2, warnings.Length);
        Assert.StartsWith($"WARN  The start of Linux the agent first ran in could not be noted in {_linux.Note} (", warnings[0], StringComparison.Ordinal);
        Assert.StartsWith($"WARN  That the wait for cloud-init is over could not be noted in {_linux.Marker} (", warnings[1], StringComparison.Ordinal);
    }

    // A container, or a kernel built without the file: every start then counts as the first, which only costs the wait.
    [Fact]
    public void WithoutAnIdOfTheStartEveryStartCountsAsTheFirst()
    {
        _linux.Put(CloudInitFiles.EnabledPath);
        _linux.Put(CloudInitFiles.ResultPath, CloudInitRoot.Clean);
        _linux.Put(CloudInitFiles.ConfigurationPath, PowerState);

        Assert.False(_linux.FirstStart().IsLaterStart);
        Assert.Equal(CloudInitWait.RestartComes, _linux.Wait().Pending());
        Assert.False(File.Exists(_linux.Note));
    }
}
