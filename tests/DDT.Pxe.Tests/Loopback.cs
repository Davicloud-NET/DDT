using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;

namespace DDT.Pxe.Tests;

// Socket tests bind 127.0.0.1 and port 0 only. A wildcard or privileged port would either need rights
// CI does not have or, on Windows, silently share a port with whatever already holds it.
internal static class Loopback
{
    public const string InterfaceName = "loopback";

    private static readonly string s_fixtures = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
        "Fixtures");

    public static ServedInterface Interface { get; } =
        new(NetworkInterface.LoopbackInterfaceIndex, InterfaceName, IPAddress.Loopback);

    public static NetworkInterfaceMap Map() => new(InterfaceName, [Interface]);

    public static IPEndPoint AnyPort => new(IPAddress.Loopback, 0);

    public static byte[] Fixture(string protocol, string name) =>
        File.ReadAllBytes(Path.Combine(s_fixtures, protocol, name + ".bin"));

    public static Socket Client(int port = 0)
    {
        Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, port));

        return socket;
    }

    public static async Task<(byte[] Datagram, IPEndPoint From)?> ReceiveAsync(Socket socket, TimeSpan timeout)
    {
        byte[] buffer = new byte[2048];
        using CancellationTokenSource cancellation = new(timeout);

        try
        {
            SocketReceiveFromResult result = await socket.ReceiveFromAsync(
                buffer,
                SocketFlags.None,
                new IPEndPoint(IPAddress.Any, 0),
                cancellation.Token);

            return (buffer[..result.ReceivedBytes], (IPEndPoint)result.RemoteEndPoint);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
