// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using DDT.ConsoleProtocol;

namespace DDT.Agent;

// Opens the TCP connections to the server itself, from a random client port, and notes how far each got. That way a
// connect timeout can say whether the name lookup, the TCP connection or the TLS handshake ran out. Each has different
// causes.
public static class ServerConnection
{
    private static readonly HttpRequestOptionsKey<Progress> s_progress = new("DDT.ServerConnection");

    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        Progress progress = new();
        context.InitialRequestMessage.Options.Set(s_progress, progress);

        IPAddress[] addresses = IPAddress.TryParse(context.DnsEndPoint.Host, out IPAddress? literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);

        progress.Resolved(addresses);

        Socket socket = new(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            BindToRandomPort(socket, () => RandomNumberGenerator.GetInt32(FirstDynamicPort, LastDynamicPort + 1));
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();

            throw;
        }

        progress.Connected(socket.RemoteEndPoint);

        return new NetworkStream(socket, ownsSocket: true);
    }

    // WinPE hands out the same client ports at every start, from 49668 on. The server still holds the connections a
    // restart never closed, and drops a new connection from such a port until the old one times out, up to 40 s.
    // A random port from the dynamic range almost never hits one. A taken or reserved port is drawn again, and after
    // a few draws Windows picks the port.
    public const int FirstDynamicPort = 49152;
    public const int LastDynamicPort = 65535;
    private const int PortDraws = 8;

    public static void BindToRandomPort(Socket socket, Func<int> drawPort)
    {
        for (int draw = 0; draw < PortDraws; draw++)
        {
            try
            {
                socket.Bind(new IPEndPoint(socket.DualMode ? IPAddress.IPv6Any : IPAddress.Any, drawPort()));

                return;
            }
            catch (SocketException exception) when (exception.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
            {
            }
        }
    }

    // What a connect timeout means for this request, or null when its connection was not opened for it.
    public static string? DescribeTimeout(HttpRequestMessage request, string server, string timeout) =>
        request.Options.TryGetValue(s_progress, out Progress? progress) ? progress.DescribeTimeout(server, timeout) : null;

    // The stage a connect timeout ran out in, or null when the request's connection was not opened for it.
    public static ConnectionStage? TimedOutStage(HttpRequestMessage request) =>
        request.Options.TryGetValue(s_progress, out Progress? progress) ? progress.TimedOutStage : null;

    private sealed class Progress
    {
        private readonly long _started = Environment.TickCount64;
        private long _resolvedAt;
        private long _connectedAt;
        private string? _addresses;
        private string? _remote;

        public void Resolved(IPAddress[] addresses)
        {
            _addresses = string.Join(", ", addresses.Select(address => address.ToString()));
            Volatile.Write(ref _resolvedAt, Environment.TickCount64);
        }

        // The socket is dual mode, so an IPv4 server shows up as an IPv4-mapped IPv6 address. It's shown as IPv4 again.
        public void Connected(EndPoint? remote)
        {
            _remote = remote is IPEndPoint { Address.IsIPv4MappedToIPv6: true } mapped
                ? new IPEndPoint(mapped.Address.MapToIPv4(), mapped.Port).ToString()
                : remote?.ToString();
            Volatile.Write(ref _connectedAt, Environment.TickCount64);
        }

        public ConnectionStage TimedOutStage =>
            Volatile.Read(ref _resolvedAt) == 0
                ? ConnectionStage.NameLookup
                : Volatile.Read(ref _connectedAt) == 0 ? ConnectionStage.Connection : ConnectionStage.SecureConnection;

        public string DescribeTimeout(string server, string timeout)
        {
            long resolvedAt = Volatile.Read(ref _resolvedAt);
            long connectedAt = Volatile.Read(ref _connectedAt);

            if (resolvedAt == 0)
            {
                return $"the name of the server at {server} could not be looked up within {timeout}";
            }

            if (connectedAt == 0)
            {
                return $"the server at {server} did not accept a connection within {timeout} (tried {_addresses}, " +
                    $"name lookup {Seconds(resolvedAt - _started)})";
            }

            return $"the server at {server} accepted a connection at {_remote} after {Seconds(connectedAt - _started)}, but the TLS " +
                $"handshake did not finish within {timeout}";
        }

        private static string Seconds(long milliseconds) =>
            string.Create(CultureInfo.InvariantCulture, $"{milliseconds / 1000.0:0.0} s");
    }
}
