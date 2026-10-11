// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using Xunit;

namespace DDT.Agent.Tests;

// How the agent in the installed Linux ends when its run is over without it: the server ended the run, or the state
// is gone. It then removes itself as after a finished run, so no root service stays on a machine without a run.
public sealed class LinuxPhaseLoopEndTests : IDisposable
{
    private readonly LinuxPhaseFixture _linux = new();

    public void Dispose() => _linux.Dispose();

    private static AgentRequestException NothingToContinue() =>
        new("The server answered 409.", "This machine has no run for the DDT service to continue. The service removes itself.", HttpStatusCode.Conflict);

    // An operator stopped the run while the machine was on its way into the installed Linux, or the server ended it
    // since. The run token on the disk is dead then, and the server's answer to it is the end, not an error to retry.
    [Theory]
    [InlineData("nothing to continue")]
    [InlineData("no run")]
    [InlineData("another run")]
    [InlineData("rejected")]
    [InlineData("next without the run")]
    [InlineData("next with another run")]
    public async Task RemovesItselfWhenTheServerNoLongerRunsTheRun(string answer)
    {
        AgentRun run = LinuxPhaseFixture.Run(LinuxPhaseFixture.Script(1));
        await _linux.HandOverAsync(run);
        ScriptedAgentServer server = new();

        _ = answer switch
        {
            "nothing to continue" => server.OnRegister(_ => throw NothingToContinue()),
            "no run" => server.OnRegister(_ => LinuxPhaseFixture.Continued() with { RunId = null, RunToken = null }),
            "another run" => server.OnRegister(_ => LinuxPhaseFixture.Continued() with { RunId = Guid.NewGuid() }),
            "rejected" => server.OnRegister(_ => LinuxPhaseFixture.Continued() with { State = MachineState.Rejected, Token = null, ResumeToken = null }),
            "next without the run" => server.OnRegister(_ => LinuxPhaseFixture.Continued()).OnNext(_ => LinuxPhaseFixture.Next("session-1", null)),
            _ => server.OnRegister(_ => LinuxPhaseFixture.Continued()).OnNext(_ => LinuxPhaseFixture.Next("session-1", run with { Id = Guid.NewGuid() })),
        };

        int exitCode = await _linux.RunAsync(server);

        // Exit code 0, so systemd neither starts the unit again nor keeps it as failed.
        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Equal(["systemctl disable ddt-agent.service", "systemctl daemon-reload"], _linux.Tools.Calls);
        Assert.Empty(server.RunReports);
        Assert.True(_linux.IsRemoved());
        Assert.Contains($"The server no longer runs run {TestRuns.RunId} on this machine.", _linux.Lines());
        Assert.Contains("The run is over here, so the agent removes itself.", _linux.Lines());
    }

    // The network may still be coming up, or a proxy may answer for the server. None of that says the run is over, and
    // the removal cannot be undone, so the agent keeps asking.
    [Theory]
    [InlineData(null)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task AServerThatCannotBeReachedIsAskedAgainAndTheRunGoesOn(HttpStatusCode? status)
    {
        AgentRun run = LinuxPhaseFixture.Run(LinuxPhaseFixture.Script(1));
        await _linux.HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => throw (status is { } code
                ? new AgentRequestException($"The server answered {(int)code}.", "The server cannot take the registration now.", code)
                : new HttpRequestException("No such host is known.")))
            .OnRegister(_ => throw new AgentTokenRejectedException("A proxy asked for a sign-in."))
            .OnRegister(_ => LinuxPhaseFixture.Continued())
            .OnNext(_ => LinuxPhaseFixture.Next("session-1", run));

        Assert.Equal(AgentExitCodes.Deployed, await _linux.RunAsync(server));

        Assert.Equal(3, server.Registrations.Count);
        Assert.All(server.Registrations, registration => Assert.Equal("run-token-1", registration.RunToken));
        Assert.Equal(DeploymentState.Done, server.RunReports[^1].State);
        Assert.Equal(2, _linux.Lines().Count(line => line.StartsWith("Cannot register with the server (", StringComparison.Ordinal)));
    }

    // The server takes no token of a run it ended. The script that runs then is stopped, as one that would run for
    // long, and the registration that follows gets the answer that ends the agent.
    [Fact]
    public async Task ARunTheServerEndsDuringAStepStopsThereAndTheAgentRemovesItself()
    {
        AgentRun run = LinuxPhaseFixture.Run(LinuxPhaseFixture.Script(1), LinuxPhaseFixture.Script(2));
        await _linux.HandOverAsync(run);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => LinuxPhaseFixture.Continued())
            .OnNext(_ => LinuxPhaseFixture.Next("session-1", run))
            .OnRegister(_ => throw NothingToContinue());
        server.AnswerRunReports = (_, _) => throw new AgentTokenRejectedException("The server refused the token.");
        _linux.Tools.Running = (_, stopped) => Task.Delay(Timeout.InfiniteTimeSpan, stopped);

        int exitCode = await _linux.RunAsync(server);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Equal(2, server.Registrations.Count);
        Assert.Contains("Step Script 1 was stopped.", _linux.Lines());
        Assert.DoesNotContain(_linux.Tools.Calls, call => call.Contains($"{LinuxPhaseFixture.Script(2).Id:D}", StringComparison.Ordinal));
        Assert.Equal(["systemctl disable ddt-agent.service", "systemctl daemon-reload"], _linux.Tools.Calls.TakeLast(2));
        Assert.True(_linux.IsRemoved());
    }

    // The unit starts an agent that finds its settings and no run, as after a removal that was cut off.
    [Fact]
    public async Task StartedWithoutARunItSaysSoAndRemovesWhatIsLeftOfItself()
    {
        await _linux.HandOverAsync(LinuxPhaseFixture.Run(LinuxPhaseFixture.Script(1)));
        _linux.Files.Discard();
        ScriptedAgentServer server = new();

        Assert.Equal(AgentExitCodes.Stopped, await _linux.RunAsync(server));

        Assert.Empty(server.Registrations);
        Assert.Contains($"There is no run to go on with in {RunFiles.StatePathIn(_linux.Paths.State)}.", _linux.Lines());
        Assert.True(_linux.IsRemoved());
    }

    // The boot environment saves the installed phase last. A state that still names it is its own to hand over again.
    [Fact]
    public async Task LeavesARunAloneThatTheBootEnvironmentStillHas()
    {
        AgentRun run = LinuxPhaseFixture.Run(LinuxPhaseFixture.Script(1));
        await _linux.HandOverAsync(run);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await _linux.Files.SaveStateAsync((await _linux.Files.LoadStateAsync(cancellationToken))! with { Phase = Contracts.Sequences.SequencePhase.LinuxBoot }, cancellationToken);
        ScriptedAgentServer server = new();

        Assert.Equal(AgentExitCodes.Stopped, await _linux.RunAsync(server));

        Assert.Empty(server.Registrations);
        Assert.Empty(_linux.Tools.Calls);
        Assert.True(File.Exists(_linux.Paths.Unit));
        Assert.Contains($"Run {TestRuns.RunId} still goes on in the Linux boot environment, which hands it over again. The agent leaves it alone.", _linux.Lines());
    }
}
