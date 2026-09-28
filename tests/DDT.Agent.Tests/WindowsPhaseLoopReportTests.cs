// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Agent.WindowsPhase;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// How the run's last report reaches the server, whatever stops or refusals come between.
public sealed class WindowsPhaseLoopReportTests : WindowsPhaseLoopTestBase
{
    [Fact]
    public async Task AFailureTheServerDidNotGetGoesOutAfterRegisteringAgain()
    {
        _toolRunner.AnswerExitCode = (_, _, _) => 1;
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run))
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-2", run));

        for (int attempt = 0; attempt <= ServerCallRules.MaxRetries; attempt++)
        {
            server.OnRunReport(DeploymentState.Failed, _ => throw new HttpRequestException("The server is restarting."));
        }

        int exitCode = await RunAsync(server);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Equal(["run-token-1", "run-token-2"], server.Registrations.Select(registration => registration.RunToken));
        Assert.Equal("run-report Failed session-2", server.Calls[^1]);
        Assert.StartsWith("The script ended with exit code 1", server.RunReports[^1].Error, StringComparison.Ordinal);
        Assert.Equal("remove", _tools.Calls[^1]);
    }

    // The server was out of reach for longer than the runner retries. The Done report goes out once it's back, and only
    // then does the agent remove itself.
    [Fact]
    public async Task ADoneReportTheServerDidNotGetGoesOutAfterRegisteringAgain()
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run))
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-2", run));

        for (int attempt = 0; attempt <= ServerCallRules.MaxRetries; attempt++)
        {
            server.OnRunReport(DeploymentState.Done, _ => throw new HttpRequestException("The server is restarting."));
        }

        server.OnRunReport(DeploymentState.Done, _ =>
        {
            _tools.Note("done report");

            return new AgentRunReportResult("session-d", "resume-d", null);
        });

        int exitCode = await RunAsync(server);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Equal(["setup finished", "done report", "remove", "reboot"], _tools.Calls);
        Assert.Equal(["run-token-1", "run-token-2"], server.Registrations.Select(registration => registration.RunToken));
        Assert.Equal("run-report Done session-2", server.Calls[^1]);
        Assert.Equal(Enumerable.Repeat(StepState.Done, 4), server.RunReports[^1].Steps.Select(step => step.State));
        Assert.Single(_toolRunner.Calls);
        Assert.False(File.Exists(RunFiles.StatePathIn(Windows)));
        Assert.False(File.Exists(RunFiles.In(Windows, Log()).TokenPath));
    }

    // Windows may restart, or the service stop, while the Done report is on its way. The next start sends it, because
    // the run token that can send it is still there.
    [Fact]
    public async Task ADoneReportAStopInterruptedGoesOutAtTheNextStart()
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer first = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run));
        first.OnRunReport(DeploymentState.Done, _ =>
        {
            first.Stop.Cancel();

            throw new OperationCanceledException(first.Stop.Token);
        });

        Assert.Equal(AgentExitCodes.Stopped, await RunAsync(first));
        Assert.Equal(["setup finished"], _tools.Calls);

        ScriptedAgentServer second = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-2", run))
            .OnRunReport(DeploymentState.Done, _ =>
            {
                _tools.Note("done report");

                return new AgentRunReportResult("session-d", "resume-d", null);
            });

        Assert.Equal(AgentExitCodes.Deployed, await RunAsync(second));
        Assert.Equal(["setup finished", "done report", "remove", "reboot"], _tools.Calls);
        Assert.NotNull(Assert.Single(second.Registrations).RunToken);
        AgentRunReport done = Assert.Single(second.RunReports);
        Assert.Equal((DeploymentState.Done, SequencePhase.Windows), (done.State, done.Phase));
        Assert.Equal(Enumerable.Repeat(StepState.Done, 4), done.Steps.Select(step => step.State));
        Assert.Single(_toolRunner.Calls);
        Assert.False(File.Exists(RunFiles.StatePathIn(Windows)));
        Assert.False(File.Exists(RunFiles.In(Windows, Log()).TokenPath));
    }

    [Fact]
    public async Task AFailureTheServerDidNotGetBeforeAStopGoesOutAtTheNextStart()
    {
        _toolRunner.AnswerExitCode = (_, _, _) => 1;
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer first = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run));
        first.OnRegister(_ =>
        {
            first.Stop.Cancel();

            throw new OperationCanceledException(first.Stop.Token);
        });

        for (int attempt = 0; attempt <= ServerCallRules.MaxRetries; attempt++)
        {
            first.OnRunReport(DeploymentState.Failed, _ => throw new HttpRequestException("The server is restarting."));
        }

        Assert.Equal(AgentExitCodes.Stopped, await RunAsync(first));

        ScriptedAgentServer second = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-2", run))
            .OnRunReport(DeploymentState.Failed, _ =>
            {
                _tools.Note("failed report");

                return new AgentRunReportResult("session-f", "resume-f", null);
            });

        Assert.Equal(AgentExitCodes.Stopped, await RunAsync(second));
        Assert.Equal(["setup finished", "failed report", "remove"], _tools.Calls);
        AgentRunReport failed = Assert.Single(second.RunReports);
        Assert.StartsWith("The script ended with exit code 1", failed.Error, StringComparison.Ordinal);
        Assert.Single(_toolRunner.Calls);
        Assert.False(File.Exists(RunFiles.StatePathIn(Windows)));
        Assert.False(File.Exists(RunFiles.In(Windows, Log()).TokenPath));
    }

    // Only a run that is over here keeps a final report, so its steps must not run again.
    [Fact]
    public async Task AFinalReportThatCannotBeReadStillEndsTheRun()
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        await File.WriteAllTextAsync(RunFiles.In(Windows, Log()).FinalReportPath, "{ not json", TestContext.Current.CancellationToken);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run));

        int exitCode = await RunAsync(server);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Equal(["remove"], _tools.Calls);
        Assert.Empty(_toolRunner.Calls);
        AgentRunReport failed = Assert.Single(server.RunReports);
        Assert.Equal((DeploymentState.Failed, WindowsPhaseLoop.FinalReportLostMessage), (failed.State, failed.Error));
        Assert.False(File.Exists(RunFiles.In(Windows, Log()).TokenPath));
    }

    [Fact]
    public async Task ARefusedTokenBeforeTheRunRegistersAgain()
    {
        AgentRun run = Run(TestRuns.Script(4, SequencePhase.Windows));
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => throw new AgentTokenRejectedException())
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-2", run));

        int exitCode = await RunAsync(server);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Equal(2, server.Registrations.Count);
        Assert.Single(_toolRunner.Calls);
    }
}
