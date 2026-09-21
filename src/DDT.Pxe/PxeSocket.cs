// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;

namespace DDT.Pxe;

internal static class PxeSocket
{
    private const int IpProtocolLevel = 0;
    private const int SioUdpConnectionReset = unchecked((int)0x9800000C);

    // IP_UNICAST_IF is 31 in ws2ipdef.h and 50 in uapi/linux/in.h.
    private static int UnicastInterfaceOption => OperatingSystem.IsWindows() ? 31 : 50;

    // One wildcard socket per port. A socket bound per interface receives no broadcasts on Linux, and
    // wildcard plus specific on the same port needs SO_REUSEADDR, which lets another process share the
    // port. ExclusiveAddressUse is not set either: it measurably fails the bind when another process
    // holds a specific address on the same port, which is the normal state of a Hyper-V host.
    public static Socket CreateListener(IPEndPoint endpoint)
    {
        Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        try
        {
            // Must precede Bind. The runtime otherwise enables IP_PKTINFO inside the first
            // ReceiveMessageFrom call, and a datagram already queued by then reports interface 0.
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
            DisableConnectionReset(socket);
            socket.Bind(endpoint);

            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    // A transfer answers from a fresh port connected to the client. The kernel then drops datagrams
    // from any other source, and a duplicated request yields a second transfer the client can tell
    // apart by port instead of two interleaved streams from port 69.
    public static Socket CreateTransfer(IPAddress localAddress, IPEndPoint client)
    {
        Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        try
        {
            DisableConnectionReset(socket);
            socket.Bind(new IPEndPoint(localAddress, 0));
            socket.Connect(client);

            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    // A limited broadcast has no route of its own, so without this the kernel picks one interface from
    // the routing table and the reply leaves on the wrong segment. Zero restores ordinary routing.
    public static void SetEgressInterface(Socket socket, int interfaceIndex) =>
        socket.SetRawSocketOption(
            IpProtocolLevel,
            UnicastInterfaceOption,
            BitConverter.GetBytes(IPAddress.HostToNetworkOrder(interfaceIndex)));

    // Without this, an ICMP port unreachable from a client that stopped listening makes the next
    // receive on the socket fail with WSAECONNRESET.
    private static void DisableConnectionReset(Socket socket)
    {
        if (OperatingSystem.IsWindows())
        {
            socket.IOControl(SioUdpConnectionReset, [0, 0, 0, 0], null);
        }
    }

    // A pause before retrying a receive that failed for no known reason, so a persistent error does not
    // spin. False means the listener is stopping.
    public static async Task<bool> PauseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public static bool IsTransient(SocketException exception) =>
        exception.SocketErrorCode is SocketError.ConnectionReset
            or SocketError.ConnectionRefused
            or SocketError.MessageSize
            or SocketError.HostUnreachable
            or SocketError.NetworkUnreachable;
}
