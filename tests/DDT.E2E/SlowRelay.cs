// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;

namespace DDT.E2E;

// Passes connections on 127.0.0.1 to the host and throttles its answers, so a stop reaches the agent mid-download.
// The traffic stays encrypted, so the host's certificate has to name 127.0.0.1.
internal sealed class SlowRelay : IAsyncDisposable
{
    private const int ChunkBytes = 64 * 1024;

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly int _hostPort;
    private readonly int _bytesPerSecond;
    private readonly Task _accepting;

    public SlowRelay(Uri host, int bytesPerSecond)
    {
        ArgumentNullException.ThrowIfNull(host);

        _hostPort = host.Port;
        _bytesPerSecond = bytesPerSecond;
        _listener.Start();
        Url = new Uri($"https://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
        _accepting = AcceptAsync();
    }

    public Uri Url { get; }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        await _accepting.ConfigureAwait(false);
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        List<Task> connections = [];

        try
        {
            while (true)
            {
                TcpClient agent = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                connections.Add(RelayAsync(agent));
                connections.RemoveAll(connection => connection.IsCompleted);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }

        await Task.WhenAll(connections).ConfigureAwait(false);
    }

    // Until either side closes the connection, or the relay stops, which closes both.
    private async Task RelayAsync(TcpClient agent)
    {
        using (agent)
        using (TcpClient host = new())
        using (CancellationTokenSource closed = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            try
            {
                await host.ConnectAsync(IPAddress.Loopback, _hostPort, closed.Token).ConfigureAwait(false);
                Task sent = PassAsync(agent.GetStream(), host.GetStream(), null, closed.Token);
                Task received = PassAsync(host.GetStream(), agent.GetStream(), _bytesPerSecond, closed.Token);
                await Task.WhenAny(sent, received).ConfigureAwait(false);
                await closed.CancelAsync().ConfigureAwait(false);
                await Task.WhenAll(sent, received).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
            {
            }
        }
    }

    // Returns when from ends. The pace never saves up: a connection that was idle is no faster afterwards.
    private static async Task PassAsync(NetworkStream from, NetworkStream to, int? bytesPerSecond, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[ChunkBytes];
        long due = TimeProvider.System.GetTimestamp();

        try
        {
            int read;

            while ((read = await from.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

                if (bytesPerSecond is { } rate)
                {
                    long now = TimeProvider.System.GetTimestamp();
                    due = Math.Max(due, now) + (read * TimeProvider.System.TimestampFrequency / rate);
                    TimeSpan wait = TimeProvider.System.GetElapsedTime(now, due);

                    if (wait > TimeSpan.Zero)
                    {
                        await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
        }
    }
}
