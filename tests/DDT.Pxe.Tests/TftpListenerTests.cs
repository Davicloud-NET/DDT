using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DDT.Protocols.Tftp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DDT.Pxe.Tests;

public sealed class TftpListenerTests : IDisposable
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(5);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ddt-tftp-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _content;

    public TftpListenerTests()
    {
        // Two windows of four at the 1380 octet cap, the last block short, which ends the transfer.
        _content = new byte[8000];
        Random.Shared.NextBytes(_content);

        // The request fixture asks for ddt/x64/wdsmgfw.efi.
        Directory.CreateDirectory(Path.Combine(_root, "ddt", "x64"));
        File.WriteAllBytes(Path.Combine(_root, "ddt", "x64", "wdsmgfw.efi"), _content);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private TftpListener Listener(bool singlePort = false, NetworkInterfaceMap? map = null)
    {
        TftpListener listener = new(
            Loopback.AnyPort,
            map ?? Loopback.Map(),
            new BootFileResolver(_root),
            TftpLimits.Default,
            maxTransfers: 8,
            singlePort,
            TimeProvider.System,
            NullLogger<TftpListener>.Instance);

        listener.Start();

        return listener;
    }

    private static byte[] Ack(ushort block)
    {
        byte[] datagram = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(datagram, (ushort)TftpOpcode.Acknowledgement);
        BinaryPrimitives.WriteUInt16BigEndian(datagram.AsSpan(2), block);

        return datagram;
    }

    private static byte[] ReadRequest(string fileName)
    {
        byte[] datagram = [0, 1, .. Encoding.ASCII.GetBytes(fileName), 0, .. "octet"u8, 0];

        return datagram;
    }

    private static ushort Opcode(byte[] datagram) => BinaryPrimitives.ReadUInt16BigEndian(datagram);

    private async Task<(byte[] Received, IPEndPoint Server)> DownloadAsync(
        TftpListener listener,
        bool repeatBeforeAcknowledging = false,
        bool repeatAfterFirstBlock = false)
    {
        using Socket client = Loopback.Client();
        byte[] request = Loopback.Fixture("Tftp", "rrq-wdsmgfw-blksize1456-window4");

        await client.SendToAsync(request, listener.LocalEndPoint);

        (byte[] Datagram, IPEndPoint From)? optionAck = await Loopback.ReceiveAsync(client, s_timeout);
        Assert.NotNull(optionAck);
        Assert.Equal((ushort)TftpOpcode.OptionAcknowledgement, Opcode(optionAck.Value.Datagram));

        string options = Encoding.ASCII.GetString(optionAck.Value.Datagram, 2, optionAck.Value.Datagram.Length - 2);
        Assert.Contains("blksize\u00001380\u0000", options, StringComparison.Ordinal);

        IPEndPoint server = optionAck.Value.From;

        // The client settled on the first answer, then its retransmitted request reached port 69.
        if (repeatBeforeAcknowledging)
        {
            await client.SendToAsync(request, listener.LocalEndPoint);
        }

        await client.SendToAsync(Ack(0), server);

        List<byte> received = [];

        for (ushort expected = 1; ; expected++)
        {
            (byte[] Datagram, IPEndPoint From)? data = await Loopback.ReceiveAsync(client, s_timeout);

            // A second transfer answering the repeat does so from its own port, which the client ignores.
            while (data is { } stray && !stray.From.Equals(server))
            {
                data = await Loopback.ReceiveAsync(client, s_timeout);
            }

            Assert.NotNull(data);
            Assert.Equal((ushort)TftpOpcode.Data, Opcode(data.Value.Datagram));
            Assert.Equal(expected, BinaryPrimitives.ReadUInt16BigEndian(data.Value.Datagram.AsSpan(2)));
            Assert.Equal(server, data.Value.From);

            received.AddRange(data.Value.Datagram.AsSpan(4));

            if (repeatAfterFirstBlock && expected == 1)
            {
                await client.SendToAsync(request, listener.LocalEndPoint);
            }

            bool last = data.Value.Datagram.Length - 4 < 1380;

            if (last || expected % 4 == 0)
            {
                await client.SendToAsync(Ack(expected), server);
            }

            if (last)
            {
                break;
            }
        }

        return ([.. received], server);
    }

    [Fact]
    public async Task ServesAFileFromAFreshPortPerTransfer()
    {
        await using TftpListener listener = Listener();

        (byte[] received, IPEndPoint server) = await DownloadAsync(listener);

        Assert.Equal(_content, received);
        Assert.NotEqual(listener.LocalEndPoint.Port, server.Port);
    }

    [Fact]
    public async Task ServesAFileFromTheListenerPortInSinglePortMode()
    {
        await using TftpListener listener = Listener(singlePort: true);

        (byte[] received, IPEndPoint server) = await DownloadAsync(listener);

        Assert.Equal(_content, received);
        Assert.Equal(listener.LocalEndPoint.Port, server.Port);
    }

    [Fact]
    public async Task KeepsTheTransferTheClientChoseWhenItsRequestIsRepeated()
    {
        await using TftpListener listener = Listener();

        (byte[] received, _) = await DownloadAsync(listener, repeatBeforeAcknowledging: true);

        // Cancelling the first transfer in favour of the repeat would strand the client on a closed port.
        Assert.Equal(_content, received);
    }

    [Fact]
    public async Task IgnoresARepeatedRequestInSinglePortModeOnceTheClientHasAnswered()
    {
        await using TftpListener listener = Listener(singlePort: true);

        (byte[] received, _) = await DownloadAsync(listener, repeatAfterFirstBlock: true);

        // A second transfer on the same port would interleave a new OACK into the data stream.
        Assert.Equal(_content, received);
    }

    [Theory]
    [InlineData(@"..\..\etc\passwd")]
    [InlineData("/etc/passwd")]
    [InlineData("does-not-exist.efi")]
    public async Task AnswersFileNotFoundWithoutSendingData(string fileName)
    {
        await using TftpListener listener = Listener();
        using Socket client = Loopback.Client();

        await client.SendToAsync(ReadRequest(fileName), listener.LocalEndPoint);

        (byte[] Datagram, IPEndPoint From)? reply = await Loopback.ReceiveAsync(client, s_timeout);

        Assert.NotNull(reply);
        Assert.Equal((ushort)TftpOpcode.Error, Opcode(reply.Value.Datagram));
        Assert.Equal((ushort)TftpErrorCode.FileNotFound, BinaryPrimitives.ReadUInt16BigEndian(reply.Value.Datagram.AsSpan(2)));
        Assert.Null(await Loopback.ReceiveAsync(client, TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public async Task RefusesAWriteRequest()
    {
        await using TftpListener listener = Listener();
        using Socket client = Loopback.Client();

        byte[] writeRequest = ReadRequest("x64/bootmgfw.efi");
        writeRequest[1] = (byte)TftpOpcode.WriteRequest;
        await client.SendToAsync(writeRequest, listener.LocalEndPoint);

        (byte[] Datagram, IPEndPoint From)? reply = await Loopback.ReceiveAsync(client, s_timeout);

        Assert.NotNull(reply);
        Assert.Equal((ushort)TftpErrorCode.IllegalOperation, BinaryPrimitives.ReadUInt16BigEndian(reply.Value.Datagram.AsSpan(2)));
    }

    [Fact]
    public async Task IgnoresARequestArrivingOnAnInterfaceThatIsNotServed()
    {
        NetworkInterfaceMap lanOnly = new("Ethernet", [new ServedInterface(99_999, "Ethernet", IPAddress.Parse("192.0.2.10"))]);
        await using TftpListener listener = Listener(map: lanOnly);
        using Socket client = Loopback.Client();

        await client.SendToAsync(Loopback.Fixture("Tftp", "rrq-wdsmgfw-blksize1456-window4"), listener.LocalEndPoint);

        Assert.Null(await Loopback.ReceiveAsync(client, TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public async Task StopsPromptlyWithATransferInFlight()
    {
        TftpListener listener = Listener();
        using Socket client = Loopback.Client();

        await client.SendToAsync(Loopback.Fixture("Tftp", "rrq-wdsmgfw-blksize1456-window4"), listener.LocalEndPoint);
        Assert.NotNull(await Loopback.ReceiveAsync(client, s_timeout));

        Task stop = listener.DisposeAsync().AsTask();

        Assert.Same(stop, await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken)));

        // The client is told rather than left to time out. A retransmitted OACK may arrive first.
        (byte[] Datagram, IPEndPoint From)? notice;

        do
        {
            notice = await Loopback.ReceiveAsync(client, s_timeout);
        }
        while (notice is { } received && Opcode(received.Datagram) == (ushort)TftpOpcode.OptionAcknowledgement);

        Assert.NotNull(notice);
        Assert.Equal((ushort)TftpOpcode.Error, Opcode(notice.Value.Datagram));
    }
}
