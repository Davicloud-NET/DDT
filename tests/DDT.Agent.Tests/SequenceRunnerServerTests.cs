// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// How a run deals with the server: refused tokens and reports, stops, and the heartbeat's beats and tokens.
public sealed class SequenceRunnerServerTests : SequenceRunnerTestBase
{
    [Fact]
    public async Task ARefusedTokenOnAStepsOwnCallEndsTheRunAsOnABeat()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .ServeFile(_image.Sha256, _image.Content)
            .OnRunReport(DeploymentState.Running, _ => new AgentRunReportResult("session-0", "resume-0", "run-token-1"))
            .OnRunUnattend(_ => throw new AgentTokenRejectedException());

        RunResult result = await RunAsync(server);

        // The registration with the run token decides whether the run continues, so its state stays.
        Assert.Equal(RunOutcome.TokenRejected, result.Outcome);
        Assert.NotNull(result.UnsentReport);
        Assert.Contains(new StepRunState(TestRuns.Unattend.Id, StepState.Failed, SequenceRunner.LostContactMessage), result.UnsentReport.Steps);
        Assert.Equal(StepState.Running, (await LoadStateAsync())?.Steps[2].State);
        Assert.Equal("run-token-1", await RunFiles.In(Windows, Log()).LoadTokenAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(server.RunReports, report => report.State != DeploymentState.Running);
    }

    [Fact]
    public async Task ARestartTheRunAskedForHappensEvenWhenTheTokenIsRefused()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ManualTimeProvider time = new();
        TaskCompletionSource scriptRuns = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource refused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _toolRunner.AnswerExitCode = (_, _, _) =>
        {
            scriptRuns.TrySetResult();
            refused.Task.Wait(cancellationToken);

            return 3010;
        };
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());
        AgentRun run = TestRuns.Run([TestRuns.Partition, TestRuns.Script(1), TestRuns.Apply, TestRuns.Unattend], _image);

        // The script blocks the thread it runs on until a beat is refused.
        Task<RunResult> running = Task.Run(() => RunAsync(server, run, new() { Time = time, HeartbeatInterval = TimeSpan.FromSeconds(10) }), cancellationToken);
        await scriptRuns.Task.WaitAsync(cancellationToken);
        server.AnswerRunReports = (_, _) =>
        {
            refused.TrySetResult();

            throw new AgentTokenRejectedException();
        };
        await time.AdvanceUntilAsync(TimeSpan.FromSeconds(10), () => refused.Task.IsCompleted);

        RunResult result = await running.WaitAsync(cancellationToken);

        // Continuing without the restart would run the next steps in the same WinPE. After the restart, the
        // registration decides whether the run continues.
        Assert.Equal(RunOutcome.Restarting, result.Outcome);
        Assert.Equal("reboot into Windows PE", _tools.Calls[^1]);
        Assert.Equal(2, (await LoadStateAsync())?.NextIndex);
    }

    [Fact]
    public async Task AStopKeepsTheRunsStateForTheNextStart()
    {
        _tools.ApplyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        Task<RunResult> run = RunAsync(server);
        await _tools.ApplyStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await server.Stop.CancelAsync();

        Assert.Equal(new RunResult(RunOutcome.Stopped), await run);
        Assert.Equal(StepState.Running, (await LoadStateAsync())?.Steps[1].State);
        Assert.DoesNotContain(server.RunReports, report => report.State != DeploymentState.Running);
    }

    [Fact]
    public async Task ARefusedTokenOnABeatEndsTheRunAndKeepsItsState()
    {
        ManualTimeProvider time = new();
        _tools.ApplyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        Task<RunResult> run = RunAsync(server, options: new() { Time = time, HeartbeatInterval = TimeSpan.FromSeconds(10) });
        await _tools.ApplyStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        server.AnswerRunReports = (_, _) => throw new AgentTokenRejectedException();
        await time.AdvanceUntilAsync(TimeSpan.FromSeconds(10), () => run.IsCompleted);

        RunResult result = await run;

        // The registration with the run token decides whether the run continues, so its state stays.
        Assert.Equal(RunOutcome.TokenRejected, result.Outcome);
        Assert.NotNull(result.UnsentReport);
        Assert.Contains(new StepRunState(TestRuns.Apply.Id, StepState.Failed, SequenceRunner.LostContactMessage), result.UnsentReport.Steps);
        Assert.Equal(StepState.Running, (await LoadStateAsync())?.Steps[1].State);
        Assert.DoesNotContain(server.RunReports, report => report.State != DeploymentState.Running);
        Assert.DoesNotContain("reboot", _tools.Calls);
    }

    [Fact]
    public async Task ARefusedBeatFailsTheRunWithTheServersReason()
    {
        ManualTimeProvider time = new();
        _tools.ApplyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        Task<RunResult> run = RunAsync(server, options: new() { Time = time, HeartbeatInterval = TimeSpan.FromSeconds(10) });
        await _tools.ApplyStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        server.AnswerRunReports = (report, token) => report.State == DeploymentState.Running
            ? throw new AgentRequestException("409", "This run is no longer running.", HttpStatusCode.Conflict)
            : new AgentRunReportResult(token, "resume", null);
        await time.AdvanceUntilAsync(TimeSpan.FromSeconds(10), () => run.IsCompleted);

        RunResult result = await run;

        Assert.Equal(RunOutcome.Failed, result.Outcome);
        AgentRunReport report = server.RunReports[^1];
        Assert.Equal((DeploymentState.Failed, "This run is no longer running."), (report.State, report.Error));
        Assert.Contains(new StepRunState(TestRuns.Apply.Id, StepState.Failed, "This run is no longer running."), report.Steps);
        Assert.False(File.Exists(StatePath));
    }

    [Fact]
    public async Task AStopAfterWindowsWasPutFirstPutsTheBootOrderBackAndKeepsTheRun()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());
        _tools.PuttingWindowsFirst = server.Stop.Cancel;

        RunResult result = await RunAsync(server);

        // The run continues after a start from the network, so its state and answer file stay, and no restart into the
        // installed Windows is due.
        Assert.Equal(new RunResult(RunOutcome.Stopped), result);
        Assert.Equal(["firmware after the answer file", "restore"], _tools.Calls[^2..]);
        Assert.Null(RestartDue());
        Assert.True(File.Exists(UnattendFile.PathIn(Windows)));
        Assert.True(File.Exists(StatePath));
        Assert.DoesNotContain(server.RunReports, report => report.State != DeploymentState.Running);
    }

    [Fact]
    public async Task ARefusedTokenOnABeatAfterWindowsWasPutFirstPutsTheBootOrderBackAndKeepsTheRun()
    {
        ManualTimeProvider time = new();
        _tools.FirmwareGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        Task<RunResult> run = RunAsync(server, options: new() { Time = time, HeartbeatInterval = TimeSpan.FromSeconds(10) });
        await _tools.FirmwareStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        server.AnswerRunReports = (_, _) => throw new AgentTokenRejectedException();
        await time.AdvanceUntilAsync(TimeSpan.FromSeconds(10), () => run.IsCompleted);

        RunResult result = await run;

        // The registration with the run token decides whether the run continues, so its state and answer file stay.
        Assert.Equal(RunOutcome.TokenRejected, result.Outcome);
        Assert.Equal(SequenceRunner.LostContactMessage, result.UnsentReport?.Error);
        Assert.Equal(["firmware after the answer file", "restore"], _tools.Calls[^2..]);
        Assert.Null(RestartDue());
        Assert.True(File.Exists(UnattendFile.PathIn(Windows)));
        Assert.True(File.Exists(StatePath));
        Assert.DoesNotContain(server.RunReports, report => report.State != DeploymentState.Running);
    }

    [Fact]
    public async Task ARefusedLogBeforeTheDoneReportUndoesTheRunAndDoesNotRestart()
    {
        // Refused once the heartbeat has ended: an operator stopped the run just before its end.
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());
        server.AnswerLogs = batch =>
        {
            if (batch.Lines.Any(line => line.Message.StartsWith("The run is done.", StringComparison.Ordinal)))
            {
                throw new AgentTokenRejectedException();
            }
        };

        RunResult result = await RunAsync(server);

        Assert.Equal(RunOutcome.TokenRejected, result.Outcome);
        Assert.Equal(["firmware after the answer file", "restore"], _tools.Calls[^2..]);
        Assert.Null(RestartDue());
        Assert.False(File.Exists(UnattendFile.PathIn(Windows)));
        Assert.False(File.Exists(StatePath));
        Assert.DoesNotContain(server.RunReports, report => report.State != DeploymentState.Running);
    }

    [Fact]
    public async Task BeatsWithTheCurrentStepAndPercentEveryInterval()
    {
        ManualTimeProvider time = new();
        _tools.ApplyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        Task<RunResult> run = RunAsync(server, options: new() { Time = time, HeartbeatInterval = TimeSpan.FromSeconds(10) });
        await _tools.ApplyStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        // The apply's 50 percent is 70 percent of the step, after the download's 40. A change sends at most one beat,
        // so a second one at the same step and percent came from the interval.
        await time.AdvanceUntilAsync(
            TimeSpan.FromSeconds(10),
            () => server.RunReports.Count(report => report.State == DeploymentState.Running
                && report.CurrentStepId == TestRuns.Apply.Id
                && report.Percent == 70) >= 2);

        _tools.ApplyGate.SetResult();

        Assert.Equal(RunOutcome.Finished, (await run.WaitAsync(TestContext.Current.CancellationToken)).Outcome);
    }

    [Fact]
    public async Task ATransientFailureOfABeatLeavesTheRunGoing()
    {
        ManualTimeProvider time = new();
        _tools.ApplyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer());

        Task<RunResult> run = RunAsync(server, options: new() { Time = time, HeartbeatInterval = TimeSpan.FromSeconds(10) });
        await _tools.ApplyStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        // A proxy restarting, then a server that took too long. Both go away on their own.
        int beats = 0;
        server.AnswerRunReports = (report, token) => report.State != DeploymentState.Running
            ? new AgentRunReportResult(token, "resume", null)
            : Interlocked.Increment(ref beats) switch
            {
                1 => throw new HttpRequestException("502", null, HttpStatusCode.BadGateway),
                2 => throw new AgentRequestException("408", null, HttpStatusCode.RequestTimeout),
                _ => new AgentRunReportResult(token, "resume", null),
            };

        await time.AdvanceUntilAsync(TimeSpan.FromSeconds(10), () => Volatile.Read(ref beats) >= 3);

        _tools.ApplyGate.SetResult();

        Assert.Equal(RunOutcome.Finished, (await run.WaitAsync(TestContext.Current.CancellationToken)).Outcome);
    }

    [Fact]
    public async Task UsesTheTokensEachReportHandsOut()
    {
        ScriptedAgentServer server = _image.Serve(new ScriptedAgentServer())
            .OnRunReport(DeploymentState.Running, _ => new AgentRunReportResult("session-1", "resume-1", null));

        await RunAsync(server);

        Assert.Contains($"open-file {_image.Sha256} 0 session-1", server.Calls);
        Assert.Contains($"run-unattend {TestRuns.Unattend.Id} session-1", server.Calls);
        Assert.Equal("session-1", _tokens.Token);
    }

    [Fact]
    public async Task KeepsTheNewestRunTokenOnTheDisk()
    {
        int issued = 0;
        ScriptedAgentServer server = new()
        {
            AnswerRunReports = (report, token) =>
                new AgentRunReportResult(token, "resume", $"run-token-{Interlocked.Increment(ref issued)}"),
        };

        RunResult result = await RunAsync(server, TestRuns.Run([TestRuns.Partition, TestRuns.Reboot]));

        Assert.Equal(RunOutcome.Restarting, result.Outcome);
        Assert.Equal($"run-token-{issued}", _tokens.RunToken);
        Assert.Equal(_tokens.RunToken, await RunFiles.In(Windows, Log()).LoadTokenAsync(TestContext.Current.CancellationToken));
    }
}
