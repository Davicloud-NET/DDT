// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using DDT.Agent.Deployment;
using DDT.Agent.InstalledPhase;
using DDT.Agent.LinuxPhase;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// The one batch of log lines an agent sends while the system stops it. The system waits for the agent meanwhile, so
// the send has an end whatever the server does.
public sealed class InstalledPhaseLastLinesTests : IDisposable
{
    private static readonly Guid s_machine = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");

    private readonly ManualTimeProvider _time = new();
    private readonly StringWriter _console = new();
    private readonly AgentLog _log;

    public InstalledPhaseLastLinesTests() => _log = new AgentLog(_time, _console);

    public void Dispose() => _console.Dispose();

    private InstalledPhaseCalls Calls(IAgentServer server, InstalledPhaseProfile profile) => new(
        server,
        new DryRunMachineIdentityReader(1),
        new InstalledPhaseConsole(null, null, null, null, _log),
        new InstalledPhaseOptions(profile, Path.GetTempPath(), TestAgents.Version, DryRun: false),
        _log,
        _time);

    private static InstalledPhaseContact Contact() =>
        new(s_machine, new DeploymentTokens("session-1", "resume-1", "run-token-1"), new DryRunMachineIdentityReader(1).Read());

    private static InstalledPhaseProfile Linux() => LinuxPhaseProfile.With(new CloudInitWait(
        new DryRunCloudInit(),
        new FirstStartNote(new DryRunCloudInit(), Path.Combine(Path.GetTempPath(), $"ddt-no-note-{Guid.NewGuid():N}"), new AgentLog(TimeProvider.System, TextWriter.Null)),
        Path.Combine(Path.GetTempPath(), $"ddt-no-marker-{Guid.NewGuid():N}"),
        new AgentLog(TimeProvider.System, TextWriter.Null),
        TimeProvider.System));

    [Fact]
    public async Task SendsWhatTheLogStillHoldsWithTheSessionsNewestToken()
    {
        ScriptedAgentServer server = new();
        _log.Information("The user-data restarts or powers off this machine.");
        _log.Information("The agent was stopped. The run goes on when the service starts again.");
        InstalledPhaseContact contact = Contact();
        contact.Tokens.Update("session-2", "resume-2");

        await Calls(server, Linux()).SendLastLinesAsync(contact);

        Assert.Equal(["log session-2"], server.Calls);
        Assert.Equal(2, server.SentLines.Count);
        Assert.Equal(0, _log.QueuedLines);
    }

    // A server that takes the connection and never answers, as one does that is itself going down.
    [Fact]
    public async Task GivesUpAfterAFewSecondsWhenTheServerDoesNotAnswer()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        using HttpAgentServer server = new(new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/"), null);
        _log.Information("A line the server never takes.");

        Task sending = Calls(server, Linux()).SendLastLinesAsync(Contact());
        using TcpClient accepted = await listener.AcceptTcpClientAsync(cancellationToken);
        Assert.False(sending.IsCompleted);

        await _time.AdvanceUntilAsync(TimeSpan.FromSeconds(1), () => sending.IsCompleted);

        await sending;
        Assert.Equal(1, _log.QueuedLines);
        Assert.True(_time.GetUtcNow() - new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) >= LinuxPhaseProfile.LastLines);
    }

    // A token the server no longer takes, or a server that is away: the agent ends all the same and says no more.
    [Fact]
    public async Task ARefusedOrFailedSendEndsQuietly()
    {
        ScriptedAgentServer refusing = new() { AnswerLogs = _ => throw new AgentTokenRejectedException() };
        ScriptedAgentServer away = new() { AnswerLogs = _ => throw new HttpRequestException("No route to host.") };
        _log.Information("A line.");

        await Calls(refusing, Linux()).SendLastLinesAsync(Contact());
        await Calls(away, Linux()).SendLastLinesAsync(Contact());

        Assert.Single(_console.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    // The service in Windows ends as it did before: its profile names no time for last lines.
    [Fact]
    public async Task APhaseWithoutTimeForLastLinesSendsNone()
    {
        ScriptedAgentServer server = new();
        _log.Information("A line.");

        await Calls(server, Linux() with { LastLines = null, Environment = AgentEnvironment.Windows, Phase = SequencePhase.Windows }).SendLastLinesAsync(Contact());

        Assert.Empty(server.Calls);
        Assert.Equal(1, _log.QueuedLines);
    }
}
