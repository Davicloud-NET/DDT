// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class AgentLoopTests : IDisposable
{
    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");

    private readonly FakeDeploymentTools _tools = new();

    public void Dispose() => _tools.Dispose();

    private static AgentRegistrationResult Registered(
        MachineState state = MachineState.Pending,
        string? token = "poll",
        string? resumeToken = "resume") =>
        new(s_machineId, state, token, resumeToken, 10, null);

    private static AgentNextResult Next(MachineState state, string token, string resumeToken = "resume") =>
        new(state, token, resumeToken, 10, null);

    // Without a keyboard, as these tests are about registering and polling.
    private (AgentLoop Loop, ImmediateTimeProvider Time) Create(ScriptedAgentServer server, IMachineIdentityReader? identity = null)
    {
        ImmediateTimeProvider time = new();
        AgentLog log = new(time, TextWriter.Null);
        ScriptedSignInPrompt nobody = new() { IsAvailable = false };

        return (TestAgents.Loop(server, nobody, _tools, log, time, identity), time);
    }

    [Fact]
    public async Task UsesTheTokenFromEachAnswerAndHoldsLogsUntilApproved()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered(token: "poll-0"))
            .OnNext(_ => Next(MachineState.Pending, "poll-1"))
            .OnNext(_ => Next(MachineState.Approved, "session-1"))
            .OnNext(_ => Next(MachineState.Approved, "session-2"));

        (AgentLoop loop, _) = Create(server);

        int exitCode = await loop.RunAsync(server.Stop.Token);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.Equal(
            ["register", "next poll-0", "next poll-1", "log session-1", "next session-1", "next session-2"],
            server.Calls);

        // The identity was logged long before the machine could send it.
        Assert.Contains(server.SentLines, line => line.Message.StartsWith("SMBIOS UUID ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RegistersAgainWithTheLatestResumeTokenAfterAPause()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered(token: "poll-1", resumeToken: "resume-1"))
            .OnNext(_ => Next(MachineState.Pending, "poll-2", "resume-2"))
            .OnNext(_ => throw new AgentTokenRejectedException())
            .OnRegister(_ => Registered(token: "poll-3", resumeToken: "resume-3"));

        (AgentLoop loop, ImmediateTimeProvider time) = Create(server);

        await loop.RunAsync(server.Stop.Token);

        Assert.Equal(["register", "next poll-1", "next poll-2", "register", "next poll-3"], server.Calls);
        Assert.Null(server.Registrations[0].ResumeToken);
        Assert.Equal("resume-2", server.Registrations[1].ResumeToken);
        Assert.Equal([TimeSpan.FromSeconds(10), AgentLimits.MinRetryDelay], time.Delays);
    }

    // The deployment setting the registration carries, which the console starts in once it knows it.
    [Fact]
    public async Task TheConsoleSpeaksTheLanguageTheServerNames()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered(MachineState.Rejected, token: null, resumeToken: null) with { ConsoleLanguage = "de" });
        ImmediateTimeProvider time = new();
        AgentLog log = new(time, TextWriter.Null);
        ConsoleStatus status = TestAgents.Status(new ScriptedSignInPrompt() { IsAvailable = false }, log);

        await TestAgents.Loop(server, status, _tools, log, time).RunAsync(server.Stop.Token);

        Assert.Equal("de", status.State.Language);
    }

    [Fact]
    public async Task StopsWhenTheMachineIsRejected()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered(MachineState.Rejected, token: null, resumeToken: null));

        (AgentLoop loop, _) = Create(server);

        Assert.Equal(AgentExitCodes.Rejected, await loop.RunAsync(server.Stop.Token));
        Assert.Equal(["register"], server.Calls);
    }

    [Fact]
    public async Task BacksOffWhileTheServerIsUnreachable()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => throw new HttpRequestException("connection refused"))
            .OnRegister(_ => throw new HttpRequestException("connection refused"))
            .OnRegister(_ => throw new HttpRequestException("connection refused"))
            .OnRegister(_ => Registered());

        (AgentLoop loop, ImmediateTimeProvider time) = Create(server);

        await loop.RunAsync(server.Stop.Token);

        Assert.Equal(
            [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)],
            time.Delays.Take(3));
    }

    [Fact]
    public async Task RetriesAnAnswerItCannotParse()
    {
        // An HTML page from a wrong URL, or a newer server reporting a state this agent does not know,
        // must not end the agent.
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => throw new System.Text.Json.JsonException("'<' is an invalid start of a value."))
            .OnRegister(_ => Registered())
            .OnNext(_ => throw new System.Text.Json.JsonException("The JSON value could not be converted."))
            .OnNext(_ => Next(MachineState.Pending, "poll"));

        (AgentLoop loop, _) = Create(server);

        Assert.Equal(AgentExitCodes.Stopped, await loop.RunAsync(server.Stop.Token));
        Assert.Equal(["register", "register", "next poll", "next poll", "next poll"], server.Calls);
    }

    [Fact]
    public async Task KeepsPollingAndKeepsTheLinesWhenTheLogCannotBeSent()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered(MachineState.Approved, token: "session-0"))
            .OnNext(_ => Next(MachineState.Approved, "session-1"))
            .OnLog(_ => throw new HttpRequestException("503"))
            .OnNext(_ => Next(MachineState.Approved, "session-2"));

        (AgentLoop loop, _) = Create(server);

        await loop.RunAsync(server.Stop.Token);

        Assert.Equal(
            ["register", "next session-0", "log session-1", "next session-1", "log session-2", "next session-2"],
            server.Calls);
        Assert.Contains(server.SentLines, line => line.Message.StartsWith("SMBIOS UUID ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RegistersAgainWhenTheLogTokenIsRefused()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered(MachineState.Approved, token: "session-0"))
            .OnNext(_ => Next(MachineState.Approved, "session-1"))
            .OnLog(_ => throw new AgentTokenRejectedException());

        (AgentLoop loop, _) = Create(server);

        await loop.RunAsync(server.Stop.Token);

        Assert.Equal(["register", "next session-0", "log session-1", "register"], server.Calls);
    }

    [Fact]
    public async Task ReadsTheIdentityAgainForEveryRegistration()
    {
        MachineIdentity beforeDhcp = new DryRunMachineIdentityReader(1).Read() with { PrimaryMac = "02DD00000001" };
        MachineIdentity afterDhcp = beforeDhcp with { PrimaryMac = "02DD00000002" };

        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => throw new HttpRequestException("network unreachable"))
            .OnRegister(_ => Registered(MachineState.Rejected, token: null, resumeToken: null));

        (AgentLoop loop, _) = Create(server, new SequenceIdentityReader(beforeDhcp, afterDhcp));

        await loop.RunAsync(server.Stop.Token);

        Assert.Equal(["02DD00000001", "02DD00000002"], server.Registrations.Select(registration => registration.PrimaryMac));
    }

    [Fact]
    public async Task ReportsTheMeasuredIdentityWhenRegistering()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered(MachineState.Rejected, token: null, resumeToken: null));

        (AgentLoop loop, _) = Create(server);

        await loop.RunAsync(server.Stop.Token);

        MachineIdentity expected = new DryRunMachineIdentityReader(1).Read();
        AgentRegistration sent = Assert.Single(server.Registrations);
        Assert.Equal(expected.SmbiosUuid, sent.SmbiosUuid);
        Assert.Equal(expected.PrimaryMac, sent.PrimaryMac);
        Assert.Equal("1.0.0-test", sent.AgentVersion);
    }

    [Fact]
    public async Task ReportsTheChassisTypeWhenRegistering()
    {
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered(MachineState.Rejected, token: null, resumeToken: null));

        (AgentLoop loop, _) = Create(server, new SequenceIdentityReader(new DryRunMachineIdentityReader(1).Read() with { ChassisType = 10 }));

        await loop.RunAsync(server.Stop.Token);

        Assert.Equal(10, Assert.Single(server.Registrations).ChassisType);
    }

    [Fact]
    public async Task ReportsTheFactsWhenRegistering()
    {
        MachineFacts facts = new() { MemoryMegabytes = 16384, TpmPresent = false, SystemVersion = "ThinkPad T14 Gen 4" };
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => Registered(MachineState.Rejected, token: null, resumeToken: null));

        (AgentLoop loop, _) = Create(server, new SequenceIdentityReader(new DryRunMachineIdentityReader(1).Read() with { Facts = facts }));

        await loop.RunAsync(server.Stop.Token);

        Assert.Same(facts, Assert.Single(server.Registrations).Facts);
    }
}
