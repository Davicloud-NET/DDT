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

        await Assert.ThrowsAsync<TaskCanceledException>(() => server.GetReleaseAsync(cancellationToken));
        await serving;
    }

    [Fact]
    public async Task ResumesAnImageWithARangeRequest()
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

        AgentImageStream image = await server.OpenImageAsync(Guid.Empty, "session", "ab12", 1000, cancellationToken);

        await using (image)
        {
            using MemoryStream received = new();
            await image.Content.CopyToAsync(received, cancellationToken);

            Assert.Equal(1000, image.Offset);
            Assert.Equal(1500, image.TotalLength);
            Assert.Equal(rest, received.ToArray());
        }

        string request = await serving;
        Assert.StartsWith($"GET /api/agents/{Guid.Empty:D}/images/ab12 HTTP/1.1", request, StringComparison.Ordinal);
        Assert.Contains("Range: bytes=1000-", request, StringComparison.Ordinal);
        Assert.Contains("Authorization: Bearer session", request, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AsksForAnImagesLengthWithItsFirstByte()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        Task<string> serving = AnswerAsync(
            listener,
            "HTTP/1.1 206 Partial Content\r\nContent-Range: bytes 0-0/5368709120\r\nContent-Length: 1\r\n",
            [0x4D],
            cancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null, s_requestTimeout);

        long? length = await server.HeadImageAsync(Guid.Empty, "session", "ab12", cancellationToken);

        Assert.Equal(5368709120, length);
        string request = await serving;
        Assert.StartsWith($"GET /api/agents/{Guid.Empty:D}/images/ab12 HTTP/1.1", request, StringComparison.Ordinal);
        Assert.Contains("Range: bytes=0-0", request, StringComparison.Ordinal);
        Assert.Contains("Authorization: Bearer session", request, StringComparison.Ordinal);
    }

    // Only the headers are read: the body of a whole image would not even fit HttpClient's buffer.
    [Fact]
    public async Task TakesTheLengthOfAWholeImageFromAServerThatIgnoresTheRange()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        Task<string> serving = AnswerAsync(listener, "HTTP/1.1 200 OK\r\nContent-Length: 5368709120\r\n", new byte[4096], cancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null, s_requestTimeout);

        long? length = await server.HeadImageAsync(Guid.Empty, "session", "ab12", cancellationToken);
        await serving;

        Assert.Equal(5368709120, length);
    }

    [Fact]
    public async Task ARefusedImageCheckCarriesTheServersProblemTitle()
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
            () => server.HeadImageAsync(Guid.Empty, "session", "ab12", cancellationToken));
        await serving;

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("The image file is missing from the server's library.", exception.ProblemTitle);
    }

    [Fact]
    public async Task ARefusalCarriesTheServersProblemTitle()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        byte[] problem = """{"title":"This machine cannot pick an image now.","status":409}"""u8.ToArray();
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        Task<string> serving = AnswerAsync(
            listener,
            $"HTTP/1.1 409 Conflict\r\nContent-Type: application/problem+json\r\nContent-Length: {problem.Length}\r\n",
            problem,
            cancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null, s_requestTimeout);

        AgentRequestException exception = await Assert.ThrowsAsync<AgentRequestException>(
            () => server.PickImageAsync(Guid.Empty, "session", new Contracts.Agents.AgentPickRequest(Guid.Empty, 0, null), cancellationToken));
        await serving;

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Equal("This machine cannot pick an image now.", exception.ProblemTitle);
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

        AgentRun started = await server.PickSequenceAsync(s_machineId, "session", new AgentRunRequest(s_stepId, 1, "PC-042"), cancellationToken);

        string request = await serving;
        Assert.StartsWith($"POST /api/agents/{s_machineId:D}/runs HTTP/1.1", request, StringComparison.Ordinal);
        Assert.Contains("Authorization: Bearer session", request, StringComparison.Ordinal);
        Assert.Contains($$"""{"sequenceId":"{{s_stepId:D}}","diskNumber":1,"computerName":"PC-042"}""", request, StringComparison.Ordinal);
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
