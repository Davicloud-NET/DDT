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
}
