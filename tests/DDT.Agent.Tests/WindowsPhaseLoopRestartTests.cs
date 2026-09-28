// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;
using DDT.Agent.WindowsPhase;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// The restarts of Windows the run asks for, which come before anything else goes on.
public sealed class WindowsPhaseLoopRestartTests : WindowsPhaseLoopTestBase
{
    [Fact]
    public async Task AWindowsPEStepAfterTheWindowsStepsFailsTheRun()
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows), TestRuns.Script(6));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run));

        int exitCode = await RunAsync(server);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Single(_toolRunner.Calls);
        Assert.Equal((DeploymentState.Failed, SequenceRunner.WindowsPEAfterWindowsMessage), (server.RunReports[^1].State, server.RunReports[^1].Error));
        Assert.Equal("remove", _tools.Calls[^1]);
    }

    [Fact]
    public async Task ARestartIsRecordedFirstAndWaitsForWindowsToStopTheService()
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows), TestRuns.Reboot, TestRuns.Script(6, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run));
        server.AnswerRunReports = (report, token) =>
        {
            if (report.Activity == RunActivity.Restarting)
            {
                _tools.Note("restarting report");
            }

            return new AgentRunReportResult(token, "resume", null);
        };

        // Five minutes never pass, so nothing asks for the restart again.
        Task<int> running = RunAsync(server, new() { Time = new ManualTimeProvider() });
        await WaitForAsync(() => _tools.Calls.Contains("reboot"));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.False(running.IsCompleted);
        await server.Stop.CancelAsync();
        Assert.Equal(AgentExitCodes.Restarting, await running);

        // The heartbeat's own beat for the new activity can leave before the runner stops it, so the same Restarting
        // report may reach the server twice.
        Assert.Equal(
            ["setup finished", "restart due", "restarting report", "reboot"],
            _tools.Calls.Where((call, index) => index == 0 || call != _tools.Calls[index - 1]));
        Assert.Equal(5, (await RunFiles.In(Windows, Log()).LoadStateAsync(TestContext.Current.CancellationToken))?.NextIndex);
    }

    // Stopped between the step that asked for the restart and the restart itself, the service starts again in the same
    // Windows, whose state says the step is done: it restarts Windows instead of going on.
    [Fact]
    public async Task AStopBeforeTheRestartRestartsWindowsAtTheNextStart()
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows), TestRuns.Reboot, TestRuns.Script(6, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer first = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run));
        first.AnswerRunReports = (report, token) =>
        {
            if (report.Activity == RunActivity.Restarting)
            {
                first.Stop.Cancel();
            }

            return new AgentRunReportResult(token, "resume", null);
        };

        Assert.Equal(AgentExitCodes.Stopped, await RunAsync(first));
        Assert.Equal(["setup finished", "restart due"], _tools.Calls);

        ScriptedAgentServer second = new();
        Task<int> running = RunAsync(second, new() { Time = new ManualTimeProvider() });
        await WaitForAsync(() => _tools.Calls.Contains("reboot"));
        await second.Stop.CancelAsync();

        Assert.Equal(AgentExitCodes.Restarting, await running);
        Assert.Empty(second.Registrations);
        Assert.Equal(["setup finished", "restart due", "reboot"], _tools.Calls);
        Assert.Single(_toolRunner.Calls);
    }

    // The server may stop taking the machine's token once the step asked for the restart, when it is told of the
    // restart or sent the last log lines, which only the runner sends then. Unlike at the hand-over, Windows still
    // restarts before the next step runs.
    [Theory]
    [InlineData("the restart")]
    [InlineData("the last log lines")]
    public async Task ARefusedTokenAtTheRestartStillRestartsWindowsBeforeTheNextStep(string refused)
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows), TestRuns.Reboot, TestRuns.Script(6, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run))
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-2", run));

        if (refused == "the restart")
        {
            server.AnswerRunReports = (report, token) => report.Activity == RunActivity.Restarting
                ? throw new AgentTokenRejectedException()
                : new AgentRunReportResult(token, "resume", null);
        }
        else
        {
            server.AnswerLogs = batch =>
            {
                if (batch.Lines.Any(line => line.Message == "Windows restarts, and the run goes on after the restart."))
                {
                    throw new AgentTokenRejectedException();
                }
            };
        }

        Task<int> running = RunAsync(server, new() { Time = new ManualTimeProvider() });
        await WaitForAsync(() => _tools.Calls.Contains("reboot"));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await server.Stop.CancelAsync();

        Assert.Equal(AgentExitCodes.Restarting, await running);
        Assert.Equal(["setup finished", "restart due", "reboot"], _tools.Calls);
        Assert.Single(_toolRunner.Calls);
    }

    // However the loop comes round again, a restart the run recorded comes first.
    [Fact]
    public async Task ARestartThatIsDueComesBeforeTheNextRegistration()
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ =>
            {
                // As a run that recorded its restart and came back without it leaves the marker.
                _tools.RestartDue = true;

                throw new AgentTokenRejectedException();
            })
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-2", run));

        Task<int> running = RunAsync(server, new() { Time = new ManualTimeProvider() });
        await WaitForAsync(() => _tools.Calls.Contains("reboot"));
        await server.Stop.CancelAsync();

        Assert.Equal(AgentExitCodes.Restarting, await running);
        Assert.Equal(["reboot"], _tools.Calls);
        Assert.Single(server.Registrations);
        Assert.Empty(_toolRunner.Calls);
    }

    [Fact]
    public async Task AsksForTheRestartAgainWhenWindowsHasNotRestartedAfterFiveMinutes()
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows), TestRuns.Reboot);
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run));
        ManualTimeProvider time = new();
        StringWriter console = new();

        Task<int> running = RunAsync(server, new() { Time = time, Log = new AgentLog(time, console) });

        // Then the loop's wait for the restart is the only timer.
        await WaitForAsync(() => Restarts() == 1 && time.PendingTimers == 1);
        time.Advance(WindowsPhaseLoop.RestartTimeout - TimeSpan.FromSeconds(1));

        Assert.Equal(1, Restarts());

        time.Advance(TimeSpan.FromSeconds(1));
        await WaitForAsync(() => Restarts() == 2);
        await server.Stop.CancelAsync();

        Assert.Equal(AgentExitCodes.Restarting, await running);
        Assert.Equal(2, Restarts());
        Assert.Contains(
            console.ToString().Split(Environment.NewLine),
            line => line.EndsWith("Windows has not restarted 5 minutes after the run asked it to. Asking again.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task JoinsTheDomainWithTheAccountOfTheRunningStepThenRestartsWindows()
    {
        AgentRun run = Run(TestRuns.Join, TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run))
            .OnRunCredentials(_ => TestRuns.JoinAccount);
        ManualTimeProvider time = new();
        StringWriter console = new();

        Task<int> running = RunAsync(server, new() { Time = time, Log = new AgentLog(time, console) });
        await WaitForAsync(() => _tools.Calls.Contains("reboot"));
        await server.Stop.CancelAsync();

        Assert.Equal(AgentExitCodes.Restarting, await running);
        Assert.Equal(["setup finished", "join corp.example.test", "restart due", "reboot"], _tools.Calls);
        Assert.Empty(_toolRunner.Calls);

        // The server hands out the account only for the step it has seen running.
        List<string> calls = server.Calls;
        int fetch = calls.IndexOf($"run-credentials {TestRuns.Join.Id} session-1");
        int reportsBefore = calls.Take(fetch).Count(call => call.StartsWith("run-report", StringComparison.Ordinal));
        Assert.Contains(new StepRunState(TestRuns.Join.Id, StepState.Running, null), server.RunReports[reportsBefore - 1].Steps);

        Assert.DoesNotContain(TestRuns.JoinAccount.Password, console.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(server.SentLines, line => line.Message.Contains(TestRuns.JoinAccount.Password, StringComparison.Ordinal));
        Assert.All(server.RunReports, report => Assert.DoesNotContain(TestRuns.JoinAccount.Password, report.Error ?? string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADryRunReturnsAtTheRestart()
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows), TestRuns.Reboot);
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run));

        Assert.Equal(AgentExitCodes.Restarting, await RunAsync(server, new() { DryRun = true }));
        Assert.Equal(["setup finished", "restart due", "reboot"], _tools.Calls);
    }
}
