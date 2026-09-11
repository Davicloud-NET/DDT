using System.Net;
using System.Net.Sockets;

namespace DDT.Pxe;

internal static class PxeSocket
{
    private const int IpProtocolLevel = 0;
    private const int SioUdpConnectionReset = unchecked((int)0x9800000C);

    // IP_UNICAST_IF is 31 in ws2ipdef.h and 50 in uapi/linux/in.h.
    private static int UnicastInterfaceOption => OperatingSystem.IsWindows() ? 31 : 50;

    // One wildcard socket per port. A socket bound per interface receives nothing on Linux, and
    // wildcard plus specific on the same port requires SO_REUSEADDR on both, which on Windows lets
    // any local process share the tuple. Neither SO_REUSEADDR nor ExclusiveAddressUse is set:
    // ExclusiveAddressUse measurably fails the bind when another process holds a specific address
    // on the same port, which is the normal state of a machine running Hyper-V.
    public static Socket CreateListener(int port)
    {
        Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        try
        {
            // Must precede Bind. The runtime otherwise enables IP_PKTINFO inside the first
            // ReceiveMessageFrom call, and a datagram already queued by then reports interface 0.
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);

            if (OperatingSystem.IsWindows())
            {
                // Without this, an ICMP port unreachable from a client that stopped listening makes
                // the next receive on this unconnected socket fail with WSAECONNRESET.
                socket.IOControl(SioUdpConnectionReset, [0, 0, 0, 0], null);
            }

            socket.Bind(new IPEndPoint(IPAddress.Any, port));

            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    // A limited broadcast has no route of its own, so without this the kernel picks one interface
    // from the routing table and the reply leaves on the wrong segment. Passing zero restores
    // ordinary routing for unicast destinations.
    public static void SetEgressInterface(Socket socket, int interfaceIndex)
    {
        ArgumentNullException.ThrowIfNull(socket);

        socket.SetRawSocketOption(
            IpProtocolLevel,
            UnicastInterfaceOption,
            BitConverter.GetBytes(IPAddress.HostToNetworkOrder(interfaceIndex)));
    }
}
