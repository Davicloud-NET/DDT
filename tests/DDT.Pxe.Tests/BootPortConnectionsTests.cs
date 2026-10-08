// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace DDT.Pxe.Tests;

// The boot port on a real Kestrel, with plain sockets as the machine: GRUB's own HTTP client is that simple.
public sealed class BootPortConnectionsTests : IAsyncLifetime
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    private readonly string _root = Directory.CreateTempSubdirectory("ddt-boot-port-").FullName;
    private readonly int _port = FreePort();
    private readonly BootConnections _connections = new(TimeProvider.System);

    private WebApplication? _app;

    public async ValueTask InitializeAsync()
    {
        // Far more than the socket buffers hold, so the answer is still being sent when the machine goes away.
        byte[] image = new byte[64 * 1024 * 1024];
        await File.WriteAllBytesAsync(Path.Combine(_root, "base.img"), image);
        await File.WriteAllTextAsync(Path.Combine(_root, "small.txt"), "small");

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(
            IPAddress.Loopback,
            _port,
            listen => listen.Use(next => BootPortConnections.Track(next, _connections))));

        _app = builder.Build();
        _app.UseRouting();
        _app.UseBootListenerIsolation(_port, Loopback.Map, _connections);
        _app.MapBootFiles(new BootFiles(new BootFileResolver(_root), null, _port, _connections, TimeProvider.System));

        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        Directory.Delete(_root, recursive: true);
    }

    private static int FreePort()
    {
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private async Task<Socket> ConnectAsync()
    {
        Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, _port), TestContext.Current.CancellationToken);

        return socket;
    }

    private static Task SendAsync(Socket socket, string path) =>
        socket.SendAsync(Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: 127.0.0.1\r\nUser-Agent: GRUB 2.12\r\n\r\n"));

    // Reads until the server closes. Returns what came, and whether the close was a reset instead of an orderly end.
    private static async Task<(string Text, bool Reset)> ReadToEndAsync(Socket socket)
    {
        using CancellationTokenSource timeout = new(s_timeout);
        byte[] buffer = new byte[65536];
        StringBuilder text = new();

        try
        {
            while (true)
            {
                int read = await socket.ReceiveAsync(buffer, SocketFlags.None, timeout.Token);

                if (read == 0)
                {
                    return (text.ToString(), false);
                }

                if (text.Length < 4096)
                {
                    text.Append(Encoding.Latin1.GetString(buffer, 0, read));
                }
            }
        }
        catch (SocketException exception) when (exception.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionAborted)
        {
            return (text.ToString(), true);
        }
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(s_timeout);

        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    [Fact]
    public async Task ClosesTheConnectionAfterItsAnswerAlthoughTheMachineAskedForNone()
    {
        using Socket machine = await ConnectAsync();
        await SendAsync(machine, "/boot/small.txt");

        (string text, bool reset) = await ReadToEndAsync(machine);

        Assert.StartsWith("HTTP/1.1 200 OK\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\r\nConnection: close\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\r\nContent-Length: 5\r\n", text, StringComparison.Ordinal);
        Assert.EndsWith("\r\n\r\nsmall", text, StringComparison.Ordinal);
        Assert.False(reset);
        await UntilAsync(() => _connections.Open == 0);
        Assert.True(_connections.HttpIsSafe(IPAddress.Loopback));
    }

    // The price of closing first: the server's system keeps the connection for a while after an answer that arrived
    // whole, and Kestrel has nothing left to reset. So the system's own list is asked before a machine is sent to HTTP.
    [Fact]
    public async Task AfterAnAnswerThatArrivedWholeTheSystemStillHoldsTheConnectionAndThatIsARisk()
    {
        Socket machine = await ConnectAsync();
        await SendAsync(machine, "/boot/small.txt");
        Assert.False((await ReadToEndAsync(machine)).Reset);
        machine.Dispose();
        await UntilAsync(() => _connections.Open == 0);

        Assert.Equal(HttpBootRisk.None, _connections.HttpRisk(IPAddress.Loopback, _port));
        await UntilAsync(() => new BootConnections(TimeProvider.System, new SystemTcpConnections(TimeProvider.System))
            .HttpRisk(IPAddress.Loopback, _port) == HttpBootRisk.ConnectionLeft);
    }

    [Fact]
    public async Task ClosesTheConnectionAfterAnAnswerThatThereIsNoSuchFile()
    {
        using Socket machine = await ConnectAsync();
        await SendAsync(machine, "/boot/linux/revocations_sbat.efi");

        (string text, bool reset) = await ReadToEndAsync(machine);

        Assert.StartsWith("HTTP/1.1 404 Not Found\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\r\nConnection: close\r\n", text, StringComparison.Ordinal);
        Assert.False(reset);
        Assert.True(_connections.HttpIsSafe(IPAddress.Loopback));
    }

    // A reset, not an orderly close: the server's system forgets the connection at once, so the same port can connect
    // again. An orderly close would go on sending to a machine that no longer listens.
    [Fact]
    public async Task AMachineThatStartsAgainHasItsOpenConnectionsReset()
    {
        using Socket idle = await ConnectAsync();
        using Socket reading = await ConnectAsync();
        await SendAsync(reading, "/boot/base.img");
        byte[] first = new byte[1024];
        Assert.True(await reading.ReceiveAsync(first, SocketFlags.None, TestContext.Current.CancellationToken) > 0);
        await UntilAsync(() => _connections.Open == 2);

        Assert.Equal(2, _connections.NetbootStarts(IPAddress.Loopback));

        Assert.True((await ReadToEndAsync(idle)).Reset);
        Assert.True((await ReadToEndAsync(reading)).Reset);
        await UntilAsync(() => _connections.Open == 0);
        Assert.False(_connections.HttpIsSafe(IPAddress.Loopback));
    }

    [Fact]
    public async Task AMachineThatGoesAwayInTheMiddleOfAReadIsRememberedForItsNextStart()
    {
        Socket machine = await ConnectAsync();
        await SendAsync(machine, "/boot/base.img");
        byte[] first = new byte[1024];
        Assert.True(await machine.ReceiveAsync(first, SocketFlags.None, TestContext.Current.CancellationToken) > 0);
        Assert.True(_connections.HttpIsSafe(IPAddress.Loopback));

        // As a machine that is reset: gone without a word, which the server learns from the reset its next block gets.
        machine.Close(0);

        await UntilAsync(() => !_connections.HttpIsSafe(IPAddress.Loopback));
        await UntilAsync(() => _connections.Open == 0);
    }

    // The host's own wiring: Kestrel reads the endpoint named Boot from configuration and tracks its connections.
    [Fact]
    public async Task TheHostsBootEndpointTracksItsConnectionsAndClosesAfterEachAnswer()
    {
        int port = FreePort();
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Configuration["DDT:Pxe:HttpBootPort"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);

        PxeHostSource source = new(
            () => new PxeDesiredSetup(1, null, null, false),
            () => new CancellationChangeToken(CancellationToken.None),
            _ => Task.CompletedTask);
        PxeBootstrap bootstrap = builder.AddDdtPxe(_root, _ => source);

        // The host listens on every address. A test stays on loopback.
        builder.Configuration[$"Kestrel:Endpoints:{PxeHostingExtensions.BootEndpointName}:Url"] = $"http://127.0.0.1:{port}";

        await using WebApplication app = builder.Build();
        app.UseRouting();
        app.UseDdtBootListener(bootstrap);
        await app.StartAsync(TestContext.Current.CancellationToken);

        BootConnections connections = app.Services.GetRequiredService<BootConnections>();
        using Socket machine = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await machine.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), TestContext.Current.CancellationToken);
        await UntilAsync(() => connections.Open == 1);

        await SendAsync(machine, "/boot/linux/vmlinuz");
        (string text, bool reset) = await ReadToEndAsync(machine);

        Assert.StartsWith("HTTP/1.1 404 Not Found\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\r\nConnection: close\r\n", text, StringComparison.Ordinal);
        Assert.False(reset);
        await UntilAsync(() => connections.Open == 0);

        using Socket again = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await again.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), TestContext.Current.CancellationToken);
        await UntilAsync(() => connections.Open == 1);

        Assert.Equal(1, connections.NetbootStarts(IPAddress.Loopback));
        Assert.True((await ReadToEndAsync(again)).Reset);
    }
}
