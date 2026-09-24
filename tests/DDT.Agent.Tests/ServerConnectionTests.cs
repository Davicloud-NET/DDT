// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using System.Text;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class ServerConnectionTests
{
    private static int FreePort()
    {
        using TcpListener probe = new(IPAddress.IPv6Any, 0);
        probe.Server.DualMode = true;
        probe.Start();

        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    [Fact]
    public void DrawsAgainWhenAPortIsTaken()
    {
        using TcpListener taken = new(IPAddress.IPv6Any, 0);
        taken.Server.DualMode = true;
        taken.Start();
        int takenPort = ((IPEndPoint)taken.LocalEndpoint).Port;
        int freePort = FreePort();
        Queue<int> draws = new([takenPort, freePort]);
        using Socket socket = new(SocketType.Stream, ProtocolType.Tcp);

        ServerConnection.BindToRandomPort(socket, draws.Dequeue);

        Assert.Equal(freePort, ((IPEndPoint)socket.LocalEndPoint!).Port);
        Assert.Empty(draws);
    }

    [Fact]
    public void LeavesThePortToWindowsWhenEveryDrawIsTaken()
    {
        using TcpListener taken = new(IPAddress.IPv6Any, 0);
        taken.Server.DualMode = true;
        taken.Start();
        int takenPort = ((IPEndPoint)taken.LocalEndpoint).Port;
        int draws = 0;
        using Socket socket = new(SocketType.Stream, ProtocolType.Tcp);

        ServerConnection.BindToRandomPort(socket, () =>
        {
            draws++;

            return takenPort;
        });

        Assert.False(socket.IsBound);
        Assert.Equal(8, draws);
    }

    // An agent that starts a newer one closes its kept-alive connection first, so a restart does not leave it open on
    // the server.
    [Fact]
    public async Task ClosingTheConnectionsEndsAKeptAliveConnection()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using HttpAgentServer server = new(new Uri($"http://127.0.0.1:{port}/"), null);

        Task<AgentRelease?> request = server.GetReleaseAsync(cancellationToken);
        using TcpClient accepted = await listener.AcceptTcpClientAsync(cancellationToken);
        NetworkStream stream = accepted.GetStream();
        byte[] buffer = new byte[4096];
        int read = 0;

        while (!Encoding.ASCII.GetString(buffer, 0, read).Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            read += await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
        }

        byte[] body = """{"sha256":"00","size":1}"""u8.ToArray();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\n\r\n"), cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await request;

        server.CloseConnections();

        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(5));
        Assert.Equal(0, await stream.ReadAsync(buffer, limit.Token));
    }

    // The agent connects from a port of the dynamic range that it drew itself, so two connections in a row do not take
    // the neighbouring ports that Windows would hand out.
    [Fact]
    public async Task TheAgentConnectsFromAPortOfTheDynamicRange()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using HttpAgentServer server = new(new Uri($"http://127.0.0.1:{port}/"), null, connectTimeout: TimeSpan.FromSeconds(5));

        Task request = server.GetReleaseAsync(cancellationToken);
        using TcpClient accepted = await listener.AcceptTcpClientAsync(cancellationToken);
        int clientPort = ((IPEndPoint)accepted.Client.RemoteEndPoint!).Port;
        accepted.Close();
        await Assert.ThrowsAnyAsync<Exception>(() => request);

        Assert.InRange(clientPort, ServerConnection.FirstDynamicPort, ServerConnection.LastDynamicPort);
    }
}
