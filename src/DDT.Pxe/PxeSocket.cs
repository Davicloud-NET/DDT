// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace DDT.Pxe;

internal static class PxeSocket
{
    private const int IpProtocolLevel = 0;
    private const int SioUdpConnectionReset = unchecked((int)0x9800000C);

    // IP_UNICAST_IF is 31 in ws2ipdef.h and 50 in uapi/linux/in.h.
    private static int UnicastInterfaceOption => OperatingSystem.IsWindows() ? 31 : 50;

    // One wildcard socket per port. A socket per interface gets no broadcasts on Linux, and mixing both needs
    // SO_REUSEADDR, which lets another process share the port. There's no ExclusiveAddressUse either. It fails the
    // bind on a Hyper-V host, where another process holds a specific address on the same port.
    public static Socket CreateListener(IPEndPoint endpoint)
    {
        Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        try
        {
            // This must come before Bind. Otherwise the runtime enables IP_PKTINFO inside the first ReceiveMessageFrom
            // call, and a datagram already queued by then reports interface 0.
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

    // A new port connected to the client. The kernel drops datagrams from other sources. A repeated request gets a
    // transfer the client can tell apart by port, instead of a second stream from port 69.
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

    // A limited broadcast isn't tied to a route. Without this, the kernel picks one interface from the routing table
    // and the reply leaves on the wrong segment. Zero restores ordinary routing.
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

    // Returns the next datagram, or null once the listener stops. A receive that fails for an unknown reason is logged
    // and retried after a pause, so a persistent error doesn't spin. The loop never gives up, because a dead listener
    // leaves every machine unable to boot.
    public static async Task<SocketReceiveMessageFromResult?> ReceiveAsync(
        Socket socket,
        byte[] buffer,
        int port,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        EndPoint anySource = new IPEndPoint(IPAddress.Any, 0);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                return await socket.ReceiveMessageFromAsync(buffer, SocketFlags.None, anySource, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (SocketException exception) when (IsTransient(exception))
            {
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (SocketException exception)
            {
                PxeLog.ReceiveFailed(logger, port, exception);

                if (!await PauseAsync(cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }
            }
        }

        return null;
    }

    public static bool IsTransient(SocketException exception) =>
        exception.SocketErrorCode is SocketError.ConnectionReset
            or SocketError.ConnectionRefused
            or SocketError.MessageSize
            or SocketError.HostUnreachable
            or SocketError.NetworkUnreachable;

    private static async Task<bool> PauseAsync(CancellationToken cancellationToken)
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
}
