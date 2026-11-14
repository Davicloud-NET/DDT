// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Net;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// The first-logon commands and the automatic logon a run in Windows sets up as it ends there.
public sealed class WindowsPhaseFirstLogonTests : WindowsPhaseLoopTestBase
{
    private const string Password = "Kiosk <pass> & \"9\"";

    private static readonly AgentFirstLogon s_firstLogon = new(["cmd /c echo one", "cmd /c echo two"], new AgentAutoLogon("kiosk", "CORP", Password, 2));

    private readonly StringWriter _logText = new();

    private AgentRun FirstLogonRun() => Run(TestRuns.Script(4, SequencePhase.Windows)) with { SetsUpFirstLogon = true };

    private async Task<(ScriptedAgentServer Server, int ExitCode)> EndAsync(
        AgentRun run,
        DeploymentState state,
        Func<Guid, AgentFirstLogon> answer,
        FakeFirstLogon firstLogon)
    {
        _toolRunner.AnswerExitCode = (_, _, _) => state == DeploymentState.Done ? 0 : 1;
        await HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Continued())
            .OnNext(_ => Next("session-1", run))
            .OnRunFirstLogon(answer)
            .OnRunReport(state, _ => new AgentRunReportResult("session-e", "resume-e", null));

        int exitCode = await RunAsync(server, new()
        {
            Session = new FakeDeploySession(_tools),
            FirstLogon = firstLogon,
            Log = new AgentLog(new ImmediateTimeProvider(), _logText),
        });

        return (server, exitCode);
    }

    [Fact]
    public async Task FetchesThemBeforeTheDoneReportAndSetsThemUpOnceTheSessionIsGone()
    {
        FakeFirstLogon firstLogon = new(_tools);

        (ScriptedAgentServer server, int exitCode) = await EndAsync(FirstLogonRun(), DeploymentState.Done, _ => s_firstLogon, firstLogon);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Equal(s_firstLogon, Assert.Single(firstLogon.SetUps));
        string[] calls = [.. _tools.Calls.Where(call => call != "reboot")];
        Assert.Equal(["end the session, signing out", "first logon", "remove"], calls[^3..]);
        int fetched = server.Calls.FindIndex(call => call.StartsWith("run-first-logon", StringComparison.Ordinal));
        int done = server.Calls.FindIndex(call => call.StartsWith("run-report Done", StringComparison.Ordinal));
        Assert.InRange(fetched, 0, done - 1);
        Assert.Contains(@"2 first-logon commands and the automatic logon of CORP\kiosk, 2 times", _logText.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Password, _logText.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedRunFetchesAndSetsUpNothing()
    {
        FakeFirstLogon firstLogon = new(_tools);

        (ScriptedAgentServer server, _) = await EndAsync(FirstLogonRun(), DeploymentState.Failed, _ => s_firstLogon, firstLogon);

        Assert.DoesNotContain(server.Calls, call => call.StartsWith("run-first-logon", StringComparison.Ordinal));
        Assert.DoesNotContain("first logon", _tools.Calls);
    }

    [Fact]
    public async Task ARunWhoseAnswerFileHoldsThemFetchesNothing()
    {
        FakeFirstLogon firstLogon = new(_tools);

        (ScriptedAgentServer server, _) = await EndAsync(FirstLogonRun() with { SetsUpFirstLogon = false }, DeploymentState.Done, _ => s_firstLogon, firstLogon);

        Assert.DoesNotContain(server.Calls, call => call.StartsWith("run-first-logon", StringComparison.Ordinal));
        Assert.DoesNotContain("first logon", _tools.Calls);
        Assert.Contains("remove", _tools.Calls);
    }

    [Fact]
    public async Task ARefusedFetchIsAWarningAndTheRunIsStillDone()
    {
        FakeFirstLogon firstLogon = new(_tools);

        (ScriptedAgentServer server, int exitCode) = await EndAsync(
            FirstLogonRun(),
            DeploymentState.Done,
            _ => throw new AgentRequestException("409", "Only this machine's run gets them, while it runs in Windows.", HttpStatusCode.Conflict),
            firstLogon);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Contains(server.Calls, call => call.StartsWith("run-report Done", StringComparison.Ordinal));
        Assert.DoesNotContain("first logon", _tools.Calls);
        Assert.Contains("could not be fetched (Only this machine's run gets them, while it runs in Windows.", _logText.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASetUpThatFailsIsAWarningAndTheAgentStillLeaves()
    {
        FakeFirstLogon firstLogon = new(_tools) { Failure = new Win32Exception(5, "The auto-logon password could not be stored (error 5).") };

        (_, int exitCode) = await EndAsync(FirstLogonRun(), DeploymentState.Done, _ => s_firstLogon, firstLogon);

        Assert.Equal(AgentExitCodes.Deployed, exitCode);
        Assert.Contains("remove", _tools.Calls);
        Assert.Contains("could not be set up (The auto-logon password could not be stored (error 5).", _logText.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Password, _logText.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NothingToSetUpLeavesWinlogonAlone()
    {
        FakeFirstLogon firstLogon = new(_tools);

        await EndAsync(FirstLogonRun(), DeploymentState.Done, _ => new AgentFirstLogon([], null), firstLogon);

        Assert.DoesNotContain("first logon", _tools.Calls);
        Assert.Contains("remove", _tools.Calls);
    }
}
