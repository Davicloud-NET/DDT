// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using System.Text;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using Xunit;
using static DDT.Agent.Tests.RawHttp;

namespace DDT.Agent.Tests;

// What a step hands the server, and what it asks about it.
public sealed class HttpAgentServerRecordTests
{
    private const string Password = "111111-222222-333333-444444-555555-666666-777777-888888";

    private static readonly Guid s_machineId = Guid.Parse("0197a3c0-0000-7000-8000-00000000000a");
    private static readonly Guid s_runId = Guid.Parse("0197a3c0-0000-7000-8000-00000000000b");
    private static readonly Guid s_protectorId = Guid.Parse("8f0a1c2d-3e4f-4a5b-9c6d-7e8f9a0b1c2d");

    private static readonly TimeSpan s_answeredInTime = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ARecoveryKeyIsPostedWithItsKind()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        Task<string> serving = AnswerAsync(listener, "HTTP/1.1 204 No Content\r\n", [], cancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null, s_answeredInTime);

        await server.SendRunRecordAsync(s_machineId, "session", s_runId, new AgentRecoveryKey(s_protectorId, Password, "C:"), cancellationToken);

        string request = await serving;
        Assert.StartsWith($"POST /api/agents/{s_machineId:D}/runs/{s_runId:D}/records HTTP/1.1", request, StringComparison.Ordinal);
        Assert.Contains("Authorization: Bearer session", request, StringComparison.Ordinal);
        Assert.Contains($$"""{"kind":"recoveryKey","protectorId":"{{s_protectorId:D}}","password":"{{Password}}","volume":"C:"}""", request, StringComparison.Ordinal);
    }

    // The server's reason reaches the step, and the password stays out of it.
    [Fact]
    public async Task ARefusedRecoveryKeyNamesTheReasonAndNotThePassword()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        byte[] problem = """{"title":"Only a running Enable BitLocker step hands over a recovery key.","status":409}"""u8.ToArray();
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        Task<string> serving = AnswerAsync(
            listener,
            $"HTTP/1.1 409 Conflict\r\nContent-Type: application/problem+json\r\nContent-Length: {problem.Length}\r\n",
            problem,
            cancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null, s_answeredInTime);
        AgentRecoveryKey key = new(s_protectorId, Password, "C:");

        AgentRequestException exception = await Assert.ThrowsAsync<AgentRequestException>(
            () => server.SendRunRecordAsync(s_machineId, "session", s_runId, key, cancellationToken));
        await serving;

        Assert.Equal("Only a running Enable BitLocker step hands over a recovery key.", exception.ProblemTitle);
        Assert.DoesNotContain(Password, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Password, key.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHardwareHashStatusIsRead()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        byte[] body = Encoding.UTF8.GetBytes("""{"state":"Failed","message":"ZtdDeviceAlreadyAssigned"}""");
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        Task<string> serving = AnswerAsync(listener, Json(body), body, cancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null, s_answeredInTime);

        AgentHardwareHashStatus status = await server.GetRunHardwareHashStatusAsync(s_machineId, "session", s_runId, cancellationToken);

        Assert.StartsWith($"GET /api/agents/{s_machineId:D}/runs/{s_runId:D}/records/hardwareHash HTTP/1.1", await serving, StringComparison.Ordinal);
        Assert.Equal(new AgentHardwareHashStatus(HardwareHashUploadState.Failed, "ZtdDeviceAlreadyAssigned"), status);
    }
}
