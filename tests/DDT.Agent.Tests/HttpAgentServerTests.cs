// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class HttpAgentServerTests
{
    private static readonly Guid s_machineId = Guid.Parse("0197a3c0-0000-7000-8000-00000000000a");
    private static readonly Guid s_runId = Guid.Parse("0197a3c0-0000-7000-8000-00000000000b");
    private static readonly Guid s_stepId = Guid.Parse("0197a3c0-0000-7000-8000-00000000000c");

    private static readonly TimeSpan s_requestTimeout = TimeSpan.FromMilliseconds(200);

    // The server's download queue, as the agent sees it: an answer that only starts after a while.
    private static readonly TimeSpan s_queueWait = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task ADownloadWaitsInTheServersQueueLongerThanARequestMay()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        byte[] content = RandomNumberGenerator.GetBytes(4096);
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        Task serving = AnswerLateAsync(listener, "application/octet-stream", content, cancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null, s_requestTimeout);
        using MemoryStream destination = new();

        await server.DownloadReleaseAsync(destination, cancellationToken);
        await serving;

        Assert.Equal(content, destination.ToArray());
    }

    [Fact]
    public async Task AnyOtherRequestGivesUpAfterTheRequestTimeout()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        Task serving = AnswerLateAsync(listener, "application/json", """{"sha256":"00","size":1}"""u8.ToArray(), cancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null, s_requestTimeout);

        ServerTimeoutException timeout = await Assert.ThrowsAsync<ServerTimeoutException>(() => server.GetReleaseAsync(cancellationToken));
        await serving;

        Assert.Equal("the server did not answer within 0.2 s", timeout.Message);
        Assert.Equal(ConnectionStage.Answer, timeout.Stage);
    }

    // The connect timeout covers the TLS handshake, which a listener that never accepts leaves unanswered once the kernel
    // has taken the TCP connection. The request timeout is far away, so only the connect timeout can end the request.
    [Fact]
    public async Task SaysWhenTheServerTookTheConnectionButNotTheTlsHandshake()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using HttpAgentServer server = new(new Uri($"https://127.0.0.1:{port}/"), null, connectTimeout: TimeSpan.FromMilliseconds(300));

        ServerTimeoutException timeout = await Assert.ThrowsAsync<ServerTimeoutException>(
            () => server.GetReleaseAsync(TestContext.Current.CancellationToken));

        Assert.Matches(
            $@"^the server at 127\.0\.0\.1:{port} accepted a connection at 127\.0\.0\.1:{port} after 0\.\d s, but the TLS handshake did not finish within 0\.3 s$",
            timeout.Message);
        Assert.Equal(ConnectionStage.SecureConnection, timeout.Stage);
    }

    // ConnectionFailure's words rely on how SocketsHttpHandler reports this, which a real connection shows.
    [Fact]
    public async Task SaysWhenNothingListensOnTheServersPort()
    {
        using TcpListener closed = new(IPAddress.Loopback, 0);
        closed.Start();
        int port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();

        using HttpAgentServer server = new(new Uri($"http://127.0.0.1:{port}/"), null);

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => server.GetReleaseAsync(TestContext.Current.CancellationToken));

        Assert.Equal($"the connection to 127.0.0.1:{port} was refused, so nothing listens on that port", refused.Message);
    }

    // A stop, such as Ctrl+C, is never reported as a timeout.
    [Fact]
    public async Task AStopWhileWaitingForTheServerStaysACancellation()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        stop.CancelAfter(s_requestTimeout);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => server.GetReleaseAsync(stop.Token));
    }

    [Fact]
    public async Task ADownloadThatStallsGivesUpAtItsDeadline()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        Task serving = StallAsync(listener, cancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null, downloadTimeout: TimeSpan.FromMilliseconds(300));
        using MemoryStream destination = new();

        ServerTimeoutException timeout = await Assert.ThrowsAsync<ServerTimeoutException>(() => server.DownloadReleaseAsync(destination, cancellationToken));
        await serving;

        Assert.Equal("the download did not finish within 0.3 s", timeout.Message);
        Assert.Equal(ConnectionStage.Answer, timeout.Stage);
    }

    // The download's own deadline is minutes away, so only the stop can end it.
    [Fact]
    public async Task AStopDuringADownloadStaysACancellation()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task serving = StallAsync(listener, TestContext.Current.CancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null);
        using MemoryStream destination = new();
        stop.CancelAfter(s_requestTimeout);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => server.DownloadReleaseAsync(destination, stop.Token));
        await serving;
    }

    // Only the headers are read: the body of a whole image would not even fit HttpClient's buffer.
    [Fact]
    public async Task TakesTheLengthOfAWholeRunFileFromAServerThatIgnoresTheRange()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        Task<string> serving = AnswerAsync(listener, "HTTP/1.1 200 OK\r\nContent-Length: 5368709120\r\n", new byte[4096], cancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null, s_requestTimeout);

        long? length = await server.HeadRunFileAsync(s_machineId, "session", s_runId, "ab12", cancellationToken);
        await serving;

        Assert.Equal(5368709120, length);
    }

    [Fact]
    public async Task ARefusedRunFileCheckCarriesTheServersProblemTitle()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        byte[] problem = """{"title":"The image file is missing from the server's library.","status":404}"""u8.ToArray();
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        Task<string> serving = AnswerAsync(
            listener,
            $"HTTP/1.1 404 Not Found\r\nContent-Type: application/problem+json\r\nContent-Length: {problem.Length}\r\n",
            problem,
            cancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null, s_requestTimeout);

        AgentRequestException exception = await Assert.ThrowsAsync<AgentRequestException>(
            () => server.HeadRunFileAsync(s_machineId, "session", s_runId, "ab12", cancellationToken));
        await serving;

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("The image file is missing from the server's library.", exception.ProblemTitle);
    }

    [Fact]
    public async Task ARefusalCarriesTheServersProblemTitle()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        byte[] problem = """{"title":"This machine cannot pick a sequence now.","status":409}"""u8.ToArray();
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        Task<string> serving = AnswerAsync(
            listener,
            $"HTTP/1.1 409 Conflict\r\nContent-Type: application/problem+json\r\nContent-Length: {problem.Length}\r\n",
            problem,
            cancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null, s_requestTimeout);

        AgentRequestException exception = await Assert.ThrowsAsync<AgentRequestException>(
            () => server.PickSequenceAsync(s_machineId, "session", new AgentRunRequest(s_stepId, 0, null), cancellationToken));
        await serving;

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Equal("This machine cannot pick a sequence now.", exception.ProblemTitle);
    }

    [Fact]
    public async Task AsksForTheSequencesTheMachineCanPick()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        byte[] body = """
            [{"id":"0197a3c0-0000-7000-8000-000000000001","name":"Install Windows","description":null,"erasesDisk":true,
              "needsComputerName":false,"requiredBytes":1000,"suggested":true}]
            """u8.ToArray();
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        Task<string> serving = AnswerAsync(listener, Json(body), body, cancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null, s_requestTimeout);

        IReadOnlyList<AgentSequenceChoice> choices = await server.GetSequencesAsync(s_machineId, "session", cancellationToken);

        string request = await serving;
        Assert.StartsWith($"GET /api/agents/{s_machineId:D}/sequences HTTP/1.1", request, StringComparison.Ordinal);
        Assert.Contains("Authorization: Bearer session", request, StringComparison.Ordinal);
        AgentSequenceChoice choice = Assert.Single(choices);
        Assert.Equal("Install Windows", choice.Name);
        Assert.True(choice.Suggested);
    }

    [Fact]
    public async Task PicksASequenceAndGetsItsRun()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        AgentRun run = new(
            s_runId,
            DeploymentState.Assigned,
            "Install Windows",
            new SequenceDefinition(SequenceDefinition.CurrentVersion, [new RebootStep { Id = s_stepId, Name = "Restart" }]),
            [],
            [],
            1,
            "PC-042");
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(run, AgentJsonContext.Default.AgentRun);
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        Task<string> serving = AnswerAsync(listener, Json(body), body, cancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null, s_requestTimeout);

        AgentRun started = await server.PickSequenceAsync(s_machineId, "session", new AgentRunRequest(s_stepId, 1, "PC-042", AllowSecureBootMismatch: true), cancellationToken);

        string request = await serving;
        Assert.StartsWith($"POST /api/agents/{s_machineId:D}/runs HTTP/1.1", request, StringComparison.Ordinal);
        Assert.Contains("Authorization: Bearer session", request, StringComparison.Ordinal);
        Assert.Contains($$"""{"sequenceId":"{{s_stepId:D}}","diskNumber":1,"computerName":"PC-042","allowSecureBootMismatch":true}""", request, StringComparison.Ordinal);
        Assert.Equal(s_runId, started.Id);
        Assert.IsType<RebootStep>(Assert.Single(started.Sequence.Steps));
    }

    [Fact]
    public async Task ReportsARunAndKeepsTheRunTokenOfTheAnswer()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        byte[] body = """{"token":"session-2","resumeToken":"resume-2","runToken":"run-1"}"""u8.ToArray();
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        Task<string> serving = AnswerAsync(listener, Json(body), body, cancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null, s_requestTimeout);

        AgentRunReportResult result = await server.ReportRunAsync(
            s_machineId,
            "session",
            s_runId,
            new AgentRunReport(
                DeploymentState.Running,
                SequencePhase.WindowsPE,
                [new StepRunState(s_stepId, StepState.Running, null)],
                s_stepId,
                40,
                RunActivity.Step,
                null),
            cancellationToken);

        string request = await serving;
        Assert.StartsWith($"POST /api/agents/{s_machineId:D}/runs/{s_runId:D}/report HTTP/1.1", request, StringComparison.Ordinal);
        Assert.Contains("Authorization: Bearer session", request, StringComparison.Ordinal);
        Assert.Contains($$"""{"state":"Running","phase":"WindowsPE","steps":[{"stepId":"{{s_stepId:D}}","state":"Running","error":null}]""", request, StringComparison.Ordinal);
        Assert.Equal(new AgentRunReportResult("session-2", "resume-2", "run-1"), result);
    }

    [Fact]
    public async Task ResumesARunFileWithARangeRequest()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        byte[] rest = RandomNumberGenerator.GetBytes(500);
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        Task<string> serving = AnswerAsync(
            listener,
            $"HTTP/1.1 206 Partial Content\r\nContent-Range: bytes 1000-1499/1500\r\nContent-Length: {rest.Length}\r\n",
            rest,
            cancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null, s_requestTimeout);

        AgentImageStream file = await server.OpenRunFileAsync(s_machineId, "session", s_runId, "ab12", 1000, cancellationToken);

        await using (file)
        {
            using MemoryStream received = new();
            await file.Content.CopyToAsync(received, cancellationToken);

            Assert.Equal(1000, file.Offset);
            Assert.Equal(1500, file.TotalLength);
            Assert.Equal(rest, received.ToArray());
        }

        string request = await serving;
        Assert.StartsWith($"GET /api/agents/{s_machineId:D}/runs/{s_runId:D}/files/ab12 HTTP/1.1", request, StringComparison.Ordinal);
        Assert.Contains("Range: bytes=1000-", request, StringComparison.Ordinal);
        Assert.Contains("Authorization: Bearer session", request, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AsksForARunFilesLengthWithItsFirstByte()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        Task<string> serving = AnswerAsync(
            listener,
            "HTTP/1.1 206 Partial Content\r\nContent-Range: bytes 0-0/5368709120\r\nContent-Length: 1\r\n",
            [0x50],
            cancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null, s_requestTimeout);

        long? length = await server.HeadRunFileAsync(s_machineId, "session", s_runId, "ab12", cancellationToken);

        Assert.Equal(5368709120, length);
        string request = await serving;
        Assert.StartsWith($"GET /api/agents/{s_machineId:D}/runs/{s_runId:D}/files/ab12 HTTP/1.1", request, StringComparison.Ordinal);
        Assert.Contains("Range: bytes=0-0", request, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchesTheAnswerFileOfARunningStep()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        byte[] body = Encoding.UTF8.GetBytes(TestImage.Unattend);
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        Task<string> serving = AnswerAsync(
            listener,
            $"HTTP/1.1 200 OK\r\nContent-Type: text/xml; charset=utf-8\r\nCache-Control: no-store\r\nContent-Length: {body.Length}\r\n",
            body,
            cancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null, s_requestTimeout);

        string unattend = await server.GetRunUnattendAsync(s_machineId, "session", s_runId, s_stepId, cancellationToken);

        string request = await serving;
        Assert.StartsWith(
            $"GET /api/agents/{s_machineId:D}/runs/{s_runId:D}/steps/{s_stepId:D}/unattend HTTP/1.1",
            request,
            StringComparison.Ordinal);
        Assert.Contains("Authorization: Bearer session", request, StringComparison.Ordinal);
        Assert.Equal(TestImage.Unattend, unattend);
    }

    [Fact]
    public async Task FetchesTheJoinAccountOfARunningStep()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        byte[] body = Encoding.UTF8.GetBytes(
            """{"domain":"corp.example.test","organizationalUnit":"OU=Workstations,DC=corp,DC=example,DC=test","userName":"CORP\\ddt-join","password":"Pa55-w0rd-never-logged"}""");
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        Task<string> serving = AnswerAsync(listener, Json(body), body, cancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null, s_requestTimeout);

        AgentJoinDomainCredentials credentials = await server.GetRunJoinCredentialsAsync(s_machineId, "session", s_runId, s_stepId, cancellationToken);

        string request = await serving;
        Assert.StartsWith(
            $"GET /api/agents/{s_machineId:D}/runs/{s_runId:D}/steps/{s_stepId:D}/credentials HTTP/1.1",
            request,
            StringComparison.Ordinal);
        Assert.Contains("Authorization: Bearer session", request, StringComparison.Ordinal);
        Assert.Equal(TestRuns.JoinAccount, credentials);
    }

    private static string Json(byte[] body) =>
        $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\n";

    private static Uri AddressOf(TcpListener listener) =>
        new($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");

    private static async Task AnswerLateAsync(TcpListener listener, string contentType, byte[] body, CancellationToken cancellationToken)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[4096];
        int read = 0;

        while (!Encoding.ASCII.GetString(buffer, 0, read).Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            int received = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
            Assert.NotEqual(0, received);
            read += received;
        }

        await Task.Delay(s_queueWait, cancellationToken);

        byte[] headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");

        try
        {
            await stream.WriteAsync(headers, cancellationToken);
            await stream.WriteAsync(body, cancellationToken);
        }
        catch (IOException)
        {
            // The client gave up waiting and closed the connection.
        }
    }

    // Answers with the headers and the first bytes of a release, then sends nothing more.
    private static async Task StallAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[4096];
        int read = 0;

        while (!Encoding.ASCII.GetString(buffer, 0, read).Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            int received = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
            Assert.NotEqual(0, received);
            read += received;
        }

        await stream.WriteAsync((byte[])[.. "HTTP/1.1 200 OK\r\nContent-Length: 4096\r\n\r\n"u8, .. new byte[16]], cancellationToken);

        try
        {
            // Until the client gives up and closes the connection.
            Assert.Equal(0, await stream.ReadAsync(buffer, cancellationToken));
        }
        catch (IOException)
        {
            // It reset the connection instead.
        }
    }

    // Answers one request at once with the given status line and headers, and returns the request, with its body.
    private static async Task<string> AnswerAsync(TcpListener listener, string head, byte[] body, CancellationToken cancellationToken)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
        NetworkStream stream = client.GetStream();
        List<byte> request = [];
        byte[] buffer = new byte[8192];

        while (!IsComplete(Encoding.UTF8.GetString([.. request])))
        {
            int received = await stream.ReadAsync(buffer, cancellationToken);
            Assert.NotEqual(0, received);
            request.AddRange(buffer.AsSpan(0, received));
        }

        // In one write, so a client that stops after the headers cannot close the connection before the body is sent.
        await stream.WriteAsync((byte[])[.. Encoding.ASCII.GetBytes($"{head}Connection: close\r\n\r\n"), .. body], cancellationToken);

        return Encoding.UTF8.GetString([.. request]);
    }

    // A JSON body goes out in chunks, other bodies with their length.
    private static bool IsComplete(string request)
    {
        int headEnd = request.IndexOf("\r\n\r\n", StringComparison.Ordinal);

        if (headEnd < 0)
        {
            return false;
        }

        string head = request[..headEnd];
        string body = request[(headEnd + 4)..];

        if (head.Contains("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase))
        {
            return body.EndsWith("0\r\n\r\n", StringComparison.Ordinal);
        }

        Match length = Regex.Match(head, @"Content-Length: (\d+)", RegexOptions.IgnoreCase);

        return !length.Success || Encoding.UTF8.GetByteCount(body) >= int.Parse(length.Groups[1].Value, CultureInfo.InvariantCulture);
    }
}
