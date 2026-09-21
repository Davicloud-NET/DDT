// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using DDT.Protocols.Dhcp;
using DDT.Protocols.Pxe;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DDT.Pxe.Tests;

public sealed class ProxyDhcpListenerTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(5);

    private static ProxyDhcpListener Listener(ProxyDhcpListenPort role, string relays = "")
    {
        NetworkInterfaceMap map = Loopback.Map();
        PxeOptions options = new()
        {
            AuthorisedRelayAgents = relays,
            BootTargets = { ["X64Uefi"] = new BootTargetOptions { Method = "Tftp", BootFile = "x64/bootmgfw.efi" } },
        };

        ProxyDhcpListener listener = new(
            role,
            Loopback.AnyPort,
            new ProxyDhcpHandler(PxeSetup.Create(options, map).ProxyDhcp, map),
            map,
            NullLogger<ProxyDhcpListener>.Instance);

        listener.Start();

        return listener;
    }

    // With ciaddr cleared the reply goes back to the datagram's source, which is the test socket.
    private static byte[] BootServerRequest()
    {
        byte[] datagram = Loopback.Fixture("Dhcp", "request-4011-uefi-x64");
        Array.Clear(datagram, 12, 4);

        return datagram;
    }

    [Fact]
    public async Task AnswersABootServerRequestOverARealSocket()
    {
        await using ProxyDhcpListener listener = Listener(ProxyDhcpListenPort.PxeBootServer);
        using Socket client = Loopback.Client();

        await client.SendToAsync(BootServerRequest(), listener.LocalEndPoint);

        (byte[] Datagram, IPEndPoint From)? reply = await Loopback.ReceiveAsync(client, s_timeout);

        Assert.NotNull(reply);
        Assert.True(DhcpMessageParser.TryParse(reply.Value.Datagram, out DhcpMessage? message, out _));
        Assert.Equal(DhcpMessageType.Ack, message.MessageType);

        // Proves the address came from IP_PKTINFO on the real datagram rather than a configured value.
        Assert.Equal(IPAddress.Loopback, message.ServerIdentifier);
    }

    [Fact]
    public async Task KeepsAnsweringAfterMalformedDatagrams()
    {
        await using ProxyDhcpListener listener = Listener(ProxyDhcpListenPort.PxeBootServer);
        using Socket client = Loopback.Client();

        await client.SendToAsync(Loopback.Fixture("Dhcp", "discover-option-length-overruns"), listener.LocalEndPoint);
        await client.SendToAsync(Loopback.Fixture("Dhcp", "discover-odd-length-architecture"), listener.LocalEndPoint);
        await client.SendToAsync(new byte[] { 1, 2, 3 }, listener.LocalEndPoint);
        await client.SendToAsync(BootServerRequest(), listener.LocalEndPoint);

        (byte[] Datagram, IPEndPoint From)? reply = await Loopback.ReceiveAsync(client, s_timeout);

        Assert.NotNull(reply);
        Assert.True(DhcpMessageParser.TryParse(reply.Value.Datagram, out DhcpMessage? message, out _));
        Assert.Equal(DhcpMessageType.Ack, message.MessageType);
    }

    [Fact]
    public async Task AnswersARelayedDiscoverAtTheRelayOnPort67()
    {
        Socket relay;

        try
        {
            relay = Loopback.Client(port: 67);
        }
        catch (SocketException exception)
        {
            Assert.Skip($"Cannot bind 127.0.0.1:67 here ({exception.SocketErrorCode}), which the relay reply is addressed to.");

            return;
        }

        using (relay)
        {
            await using ProxyDhcpListener listener = Listener(ProxyDhcpListenPort.Dhcp, relays: "127.0.0.1");

            byte[] discover = Loopback.Fixture("Dhcp", "discover-uefi-x64-pxeclient");
            IPAddress.Loopback.GetAddressBytes().CopyTo(discover, 24);

            await relay.SendToAsync(discover, listener.LocalEndPoint);

            (byte[] Datagram, IPEndPoint From)? reply = await Loopback.ReceiveAsync(relay, s_timeout);

            Assert.NotNull(reply);
            Assert.True(DhcpMessageParser.TryParse(reply.Value.Datagram, out DhcpMessage? message, out _));
            Assert.Equal(DhcpMessageType.Offer, message.MessageType);
            Assert.Equal(IPAddress.Loopback, message.GatewayAddress);
            Assert.True(message.IsBroadcastRequested);
        }
    }

    [Fact]
    public async Task StaysSilentOnAnInterfaceThatIsNotServed()
    {
        NetworkInterfaceMap map = new("Ethernet", [new ServedInterface(99_999, "Ethernet", IPAddress.Parse("192.0.2.10"))]);
        PxeOptions options = new()
        {
            BootTargets = { ["X64Uefi"] = new BootTargetOptions { Method = "Tftp", BootFile = "x64/bootmgfw.efi" } },
        };

        await using ProxyDhcpListener listener = new(
            ProxyDhcpListenPort.PxeBootServer,
            Loopback.AnyPort,
            new ProxyDhcpHandler(PxeSetup.Create(options, map).ProxyDhcp, map),
            map,
            NullLogger<ProxyDhcpListener>.Instance);
        listener.Start();

        using Socket client = Loopback.Client();
        await client.SendToAsync(BootServerRequest(), listener.LocalEndPoint);

        Assert.Null(await Loopback.ReceiveAsync(client, TimeSpan.FromMilliseconds(500)));
    }
}
