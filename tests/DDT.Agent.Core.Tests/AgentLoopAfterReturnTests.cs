// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using Xunit;

namespace DDT.Agent.Tests;

// The loop's registrations after the boot environment found a run that was handed over and did not restart for it.
public sealed class AgentLoopAfterReturnTests : IDisposable
{
    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");

    private readonly FakeDeploymentTools _tools = new();

    public void Dispose() => _tools.Dispose();

    private static AgentRegistrationResult Registered(string token, string resumeToken) => new(s_machineId, MachineState.Pending, token, resumeToken, 10, null);

    // A server that refuses the first poll's token, so the loop registers a second time and then stops.
    private static ScriptedAgentServer RegistersTwice() => new ScriptedAgentServer()
        .OnRegister(_ => Registered("poll-1", "resume-1"))
        .OnNext(_ => throw new AgentTokenRejectedException())
        .OnRegister(_ => Registered("poll-2", "resume-2"));

    private async Task<ScriptedAgentServer> RunAsync(Func<TestMachine, TestMachine> found)
    {
        ScriptedAgentServer server = RegistersTwice();
        ImmediateTimeProvider time = new();
        TestMachine machine = found(new TestMachine(_tools, new AgentLog(time, TextWriter.Null), time) { Environment = AgentEnvironment.LinuxBoot });

        await TestAgents.Loop(server, new ScriptedSignInPrompt { IsAvailable = false }, machine).RunAsync(server.Stop.Token);

        return server;
    }

    [Fact]
    public async Task ARegistrationAfterAnyOtherStartSaysNothingOfIt()
    {
        ScriptedAgentServer server = await RunAsync(machine => machine);

        Assert.All(server.Registrations, registration => Assert.Null(registration.StartsOver));
        Assert.All(server.Registrations, registration => Assert.Null(registration.HandedOver));
        Assert.Null(server.Registrations[0].ResumeToken);
    }

    // The look's own registration already has the machine as the server's, also the one that said someone started over.
    [Fact]
    public async Task GoesOnFromTheResumeTokenOfTheRegistrationBeforeTheLoopOnce()
    {
        ScriptedAgentServer server = await RunAsync(machine => machine with { ResumeToken = "resume-0" });

        Assert.Equal(["resume-0", "resume-1"], server.Registrations.Select(registration => registration.ResumeToken));
        Assert.All(server.Registrations, registration => Assert.Null(registration.RunToken));
        Assert.All(server.Registrations, registration => Assert.Null(registration.StartsOver));
        Assert.All(server.Registrations, registration => Assert.Null(registration.HandedOver));
    }
}
