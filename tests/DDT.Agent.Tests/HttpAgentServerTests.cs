using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class HttpAgentServerTests
{
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
    public async Task AskingForAnImagesLengthSendsHead()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        Task<string> serving = AnswerAsync(listener, "HTTP/1.1 200 OK\r\nContent-Length: 5368709120\r\n", [], cancellationToken);
        using HttpAgentServer server = new(AddressOf(listener), null, s_requestTimeout);

        long? length = await server.HeadImageAsync(Guid.Empty, "session", "ab12", cancellationToken);

        Assert.Equal(5368709120, length);
        Assert.StartsWith($"HEAD /api/agents/{Guid.Empty:D}/images/ab12 HTTP/1.1", await serving, StringComparison.Ordinal);
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

    // Answers one request at once with the given status line and headers, and returns the request's head.
    private static async Task<string> AnswerAsync(TcpListener listener, string head, byte[] body, CancellationToken cancellationToken)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[8192];
        int read = 0;

        while (!Encoding.ASCII.GetString(buffer, 0, read).Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            int received = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
            Assert.NotEqual(0, received);
            read += received;
        }

        await stream.WriteAsync(Encoding.ASCII.GetBytes($"{head}Connection: close\r\n\r\n"), cancellationToken);
        await stream.WriteAsync(body, cancellationToken);

        return Encoding.ASCII.GetString(buffer, 0, read);
    }
}
