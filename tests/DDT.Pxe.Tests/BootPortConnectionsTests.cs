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

// The boot port on a real Kestrel, with plain sockets as the machine. One folder is the boot directory and the bundle.
public sealed class BootPortConnectionsTests : IAsyncLifetime
{
    private const string Large = "base.img";
    private const string Small = "small.img";

    // Far more than the socket buffers hold, so the answer is still being sent when the machine goes away.
    private const int LargeLength = 64 * 1024 * 1024;

    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

    private readonly string _root = Directory.CreateTempSubdirectory("ddt-boot-port-").FullName;
    private readonly int _port = FreePort();
    private readonly BootConnections _connections = new(TimeProvider.System);
    private readonly FakeLinuxBootSite _site = new();

    private WebApplication? _app;

    public async ValueTask InitializeAsync()
    {
        await File.WriteAllBytesAsync(Path.Combine(_root, Large), new byte[LargeLength]);
        await File.WriteAllTextAsync(Path.Combine(_root, Small), "small");
        _site.Current = TestBundle.Of(_root, Large, Small);

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(
            IPAddress.Loopback,
            _port,
            listen => listen.Use(next => BootPortConnections.Track(next, _connections))));

        _app = builder.Build();
        _app.UseRouting();
        _app.UseBootListenerIsolation(_port, Loopback.Map, _connections);
        _app.MapBootFiles(new BootFiles(new BootFileResolver(_root), _site, _port, _connections, TimeProvider.System));

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

    private Task<Socket> ConnectAsync() => ConnectAsync(_port);

    private static async Task<Socket> ConnectAsync(int port)
    {
        Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), TestContext.Current.CancellationToken);

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

    // Reads one answer from a connection the server keeps open: its head, and as many octets after it as it announces.
    private static async Task<string> ReadAnswerAsync(Socket socket)
    {
        using CancellationTokenSource timeout = new(s_timeout);
        byte[] buffer = new byte[65536];
        StringBuilder head = new();
        long? left = null;

        while (left is null or > 0)
        {
            int read = await socket.ReceiveAsync(buffer, SocketFlags.None, timeout.Token);
            Assert.True(read > 0, "The server closed a connection it should have kept.");

            if (left is not null)
            {
                left -= read;

                continue;
            }

            head.Append(Encoding.Latin1.GetString(buffer, 0, read));
            string text = head.ToString();
            int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);

            if (end >= 0)
            {
                string length = text[..end].Split("\r\n").Single(line => line.StartsWith("Content-Length: ", StringComparison.Ordinal))["Content-Length: ".Length..];
                left = long.Parse(length, System.Globalization.CultureInfo.InvariantCulture) - (text.Length - end - 4);
                head.Length = end;
            }
        }

        Assert.Equal(0, left);

        return head.ToString();
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
    public async Task ClosesTheConnectionAfterALinuxAnswerAlthoughTheMachineAskedForNone()
    {
        using Socket machine = await ConnectAsync();
        await SendAsync(machine, $"/boot/linux/{Small}");

        (string text, bool reset) = await ReadToEndAsync(machine);

        Assert.StartsWith("HTTP/1.1 200 OK\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\r\nConnection: close\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\r\nContent-Length: 5\r\n", text, StringComparison.Ordinal);
        Assert.EndsWith("\r\n\r\nsmall", text, StringComparison.Ordinal);
        Assert.False(reset);
        await UntilAsync(() => _connections.Open == 0);
        Assert.True(_connections.HttpIsSafe(IPAddress.Loopback));
    }

    // Firmware that loads Windows PE over HTTP asks for file after file on one connection, as it always could.
    [Fact]
    public async Task KeepsTheConnectionOfAWindowsPEReadOpenForTheNextRequest()
    {
        using Socket machine = await ConnectAsync();

        foreach (int _ in Enumerable.Range(0, 3))
        {
            await SendAsync(machine, $"/boot/{Small}");
            string head = await ReadAnswerAsync(machine);

            Assert.StartsWith("HTTP/1.1 200 OK\r\n", head, StringComparison.Ordinal);
            Assert.DoesNotContain("Connection:", head, StringComparison.OrdinalIgnoreCase);
        }

        await SendAsync(machine, "/boot/x64/no-such-file.efi");
        string missing = await ReadAnswerAsync(machine);

        Assert.StartsWith("HTTP/1.1 404 Not Found\r\n", missing, StringComparison.Ordinal);
        Assert.DoesNotContain("Connection:", missing, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _connections.Open);
    }

    // The price of closing first: the server's system keeps the connection for a while after an answer that arrived
    // whole, and Kestrel has nothing left to reset. So the system's own list is asked before a machine is sent to HTTP.
    [Fact]
    public async Task AfterAnAnswerThatArrivedWholeTheSystemStillHoldsTheConnectionAndThatIsARisk()
    {
        Socket machine = await ConnectAsync();
        await SendAsync(machine, $"/boot/linux/{Small}");
        Assert.False((await ReadToEndAsync(machine)).Reset);
        machine.Dispose();
        await UntilAsync(() => _connections.Open == 0);

        Assert.Equal(HttpBootRisk.None, _connections.HttpRisk(IPAddress.Loopback, _port));
        await UntilAsync(() => new BootConnections(TimeProvider.System, new SystemTcpConnections(TimeProvider.System))
            .HttpRisk(IPAddress.Loopback, _port) == HttpBootRisk.ConnectionLeft);
    }

    [Fact]
    public async Task ClosesTheConnectionAfterAnAnswerThatThereIsNoSuchLinuxFile()
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
    public async Task AMachineThatStartsAgainHasItsLinuxReadsReset()
    {
        using Socket first = await ConnectAsync();
        using Socket second = await ConnectAsync();
        byte[] some = new byte[1024];

        foreach (Socket reading in new[] { first, second })
        {
            await SendAsync(reading, $"/boot/linux/{Large}");
            Assert.True(await reading.ReceiveAsync(some, SocketFlags.None, TestContext.Current.CancellationToken) > 0);
        }

        await UntilAsync(() => _connections.Open == 2);

        Assert.Equal(2, _connections.NetbootStarts(IPAddress.Loopback));

        Assert.True((await ReadToEndAsync(first)).Reset);
        Assert.True((await ReadToEndAsync(second)).Reset);
        await UntilAsync(() => _connections.Open == 0);
        Assert.False(_connections.HttpIsSafe(IPAddress.Loopback));
    }

    // A boot.wim takes minutes, and no datagram may end it: only connections that carried Linux content are reset.
    [Fact]
    public async Task AWindowsPEReadAndAConnectionThatAskedNothingAreLeftAloneWhenTheMachineStartsAgain()
    {
        using Socket idle = await ConnectAsync();
        using Socket windowsPE = await ConnectAsync();
        using Socket linux = await ConnectAsync();
        await SendAsync(windowsPE, $"/boot/{Large}");
        await SendAsync(linux, $"/boot/linux/{Large}");
        Assert.True(await linux.ReceiveAsync(new byte[1024], SocketFlags.None, TestContext.Current.CancellationToken) > 0);
        await UntilAsync(() => _connections.Open == 1);

        Assert.Equal(1, _connections.NetbootStarts(IPAddress.Loopback));

        Assert.True((await ReadToEndAsync(linux)).Reset);
        Assert.StartsWith("HTTP/1.1 200 OK\r\n", await ReadAnswerAsync(windowsPE), StringComparison.Ordinal);

        // Both still answer, on the connections they had.
        foreach (Socket kept in new[] { windowsPE, idle })
        {
            await SendAsync(kept, $"/boot/{Small}");
            Assert.StartsWith("HTTP/1.1 200 OK\r\n", await ReadAnswerAsync(kept), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AMachineThatGoesAwayInTheMiddleOfALinuxReadIsRememberedForItsNextStart()
    {
        Socket machine = await ConnectAsync();
        await SendAsync(machine, $"/boot/linux/{Large}");
        byte[] first = new byte[1024];
        Assert.True(await machine.ReceiveAsync(first, SocketFlags.None, TestContext.Current.CancellationToken) > 0);
        Assert.True(_connections.HttpIsSafe(IPAddress.Loopback));

        // As a machine that is reset: gone without a word, which the server learns from the reset its next block gets.
        machine.Close(0);

        await UntilAsync(() => !_connections.HttpIsSafe(IPAddress.Loopback));
        await UntilAsync(() => _connections.Open == 0);
    }

    // The host's own wiring: Kestrel reads the endpoint named Boot from configuration and hands each connection over.
    // This host serves no interface, so the test marks the connection as the boot port does for a file GRUB asks for.
    [Fact]
    public async Task TheHostsBootEndpointKnowsEachConnectionAndResetsOneThatCarriedLinux()
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

        TaskCompletionSource<IPAddress?> marked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using WebApplication app = builder.Build();
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/boot/linux"))
            {
                BootClient? client = context.Features.Get<BootClient>();
                client?.CarriesLinux();
                marked.TrySetResult(client?.Address);
                await release.Task;
            }

            await next(context);
        });
        app.UseDdtBootListener(bootstrap);
        await app.StartAsync(TestContext.Current.CancellationToken);

        BootConnections connections = app.Services.GetRequiredService<BootConnections>();
        using Socket windowsPE = await ConnectAsync(port);
        using Socket linux = await ConnectAsync(port);
        await SendAsync(linux, "/boot/linux/vmlinuz");

        Assert.Equal(IPAddress.Loopback, await marked.Task.WaitAsync(s_timeout, TestContext.Current.CancellationToken));
        Assert.Equal(1, connections.Open);
        Assert.Equal(1, connections.NetbootStarts(IPAddress.Loopback));
        Assert.True((await ReadToEndAsync(linux)).Reset);
        release.TrySetResult();
        await UntilAsync(() => connections.Open == 0);

        // What Windows PE's firmware would ask for is answered on a connection that stays, as before.
        foreach (int _ in Enumerable.Range(0, 2))
        {
            await SendAsync(windowsPE, "/boot/x64/bootmgfw.efi");
            string answer = await ReadAnswerAsync(windowsPE);

            Assert.StartsWith("HTTP/1.1 404 Not Found\r\n", answer, StringComparison.Ordinal);
            Assert.DoesNotContain("Connection:", answer, StringComparison.OrdinalIgnoreCase);
        }
    }
}
