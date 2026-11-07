// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Protocols.Dhcp;
using DDT.Protocols.Pxe;
using Xunit;

namespace DDT.Pxe.Tests;

public sealed class ProxyDhcpHandlerTests
{
    private const int ServedIndex = 25;
    private const int LanIndex = 20;

    private static readonly IPAddress s_servedAddress = IPAddress.Parse("172.24.64.1");
    private static readonly IPAddress s_secondaryAddress = IPAddress.Parse("172.24.64.2");
    private static readonly IPAddress s_relay = IPAddress.Parse("192.0.2.1");

    private static ProxyDhcpHandler Handler(string relays = "", BootConnections? connections = null)
    {
        NetworkInterfaceMap map = new(
            "vEthernet (Default Switch)",
            [
                new ServedInterface(LanIndex, "Ethernet", IPAddress.Parse("10.0.4.144")),
                new ServedInterface(ServedIndex, "vEthernet (Default Switch)", [s_servedAddress, s_secondaryAddress]),
            ]);

        PxeOptions options = new()
        {
            AuthorisedRelayAgents = relays,
            BootTargets = { ["X64Uefi"] = new BootTargetOptions { Method = "Tftp", BootFile = "x64/bootmgfw.efi" } },
        };

        return new ProxyDhcpHandler(PxeSetup.Create(options, map).ProxyDhcp, map, connections);
    }

    // ciaddr is at octet 12 of a DHCP message. Anyone can write any address there.
    private static byte[] WithClientAddress(byte[] datagram, IPAddress client)
    {
        client.GetAddressBytes().CopyTo(datagram, 12);

        return datagram;
    }

    private static byte[] WithGateway(byte[] datagram, IPAddress gateway)
    {
        gateway.GetAddressBytes().CopyTo(datagram, 24);

        return datagram;
    }

    private static DhcpMessage ParseReply(byte[] reply, ProxyDhcpOutcome outcome)
    {
        Assert.True(DhcpMessageParser.TryParse(reply.AsSpan(0, outcome.Length), out DhcpMessage? message, out _));

        return message;
    }

    [Fact]
    public void TakesOption54FromTheArrivalInterfaceForABroadcastDiscover()
    {
        byte[] reply = new byte[1500];

        ProxyDhcpOutcome outcome = Handler().Handle(
            Loopback.Fixture("Dhcp", "discover-uefi-x64-pxeclient"),
            ProxyDhcpListenPort.Dhcp,
            new DatagramArrival(ServedIndex, IPAddress.Broadcast, new IPEndPoint(IPAddress.Any, 68)),
            reply);

        Assert.Equal(ProxyDhcpOutcomeKind.Replied, outcome.Kind);

        // The IP destination of a DISCOVER is 255.255.255.255. Putting that in option 54 or siaddr
        // would send the client's next request nowhere.
        DhcpMessage parsed = ParseReply(reply, outcome);
        Assert.Equal(s_servedAddress, parsed.ServerIdentifier);
        Assert.Equal(new DhcpReplyDestination(IPAddress.Broadcast, 68), outcome.Reply!.Destination);
        Assert.Equal(ServedIndex, outcome.EgressInterface);
    }

    [Fact]
    public void IgnoresADatagramFromAnInterfaceThatIsNotServed()
    {
        ProxyDhcpOutcome outcome = Handler().Handle(
            Loopback.Fixture("Dhcp", "discover-uefi-x64-pxeclient"),
            ProxyDhcpListenPort.Dhcp,
            new DatagramArrival(LanIndex, IPAddress.Broadcast, new IPEndPoint(IPAddress.Any, 68)),
            new byte[1500]);

        Assert.Equal(ProxyDhcpOutcomeKind.InterfaceNotServed, outcome.Kind);
    }

    [Fact]
    public void AnswersABootServerRequestFromTheAddressTheClientUsedAndLeavesRoutingAlone()
    {
        byte[] reply = new byte[1500];

        ProxyDhcpOutcome outcome = Handler().Handle(
            Loopback.Fixture("Dhcp", "request-4011-uefi-x64"),
            ProxyDhcpListenPort.PxeBootServer,
            new DatagramArrival(ServedIndex, s_servedAddress, new IPEndPoint(IPAddress.Parse("192.0.2.55"), 4011)),
            reply);

        Assert.Equal(ProxyDhcpOutcomeKind.Replied, outcome.Kind);
        Assert.Equal(s_servedAddress, ParseReply(reply, outcome).ServerIdentifier);
        Assert.Equal(new DhcpReplyDestination(IPAddress.Parse("192.0.2.55"), 4011), outcome.Reply!.Destination);
        Assert.Equal(0, outcome.EgressInterface);
    }

    [Fact]
    public void RepliesToTheAuthorisedRelayOnPort67()
    {
        byte[] reply = new byte[1500];

        ProxyDhcpOutcome outcome = Handler(relays: "192.0.2.1").Handle(
            WithGateway(Loopback.Fixture("Dhcp", "discover-uefi-x64-pxeclient"), s_relay),
            ProxyDhcpListenPort.Dhcp,
            new DatagramArrival(ServedIndex, s_servedAddress, new IPEndPoint(s_relay, 67)),
            reply);

        Assert.Equal(ProxyDhcpOutcomeKind.Replied, outcome.Kind);
        Assert.Equal(new DhcpReplyDestination(s_relay, 67), outcome.Reply!.Destination);
        Assert.Equal(0, outcome.EgressInterface);
    }

    [Fact]
    public void AnswersFromTheSecondaryAddressARelayWasPointedAt()
    {
        byte[] reply = new byte[1500];

        ProxyDhcpOutcome outcome = Handler(relays: "192.0.2.1").Handle(
            WithGateway(Loopback.Fixture("Dhcp", "discover-uefi-x64-pxeclient"), s_relay),
            ProxyDhcpListenPort.Dhcp,
            new DatagramArrival(ServedIndex, s_secondaryAddress, new IPEndPoint(s_relay, 67)),
            reply);

        Assert.Equal(ProxyDhcpOutcomeKind.Replied, outcome.Kind);
        Assert.Equal(s_secondaryAddress, ParseReply(reply, outcome).ServerIdentifier);
    }

    [Fact]
    public void StaysSilentForARelayThatIsNotAuthorised()
    {
        ProxyDhcpOutcome outcome = Handler().Handle(
            WithGateway(Loopback.Fixture("Dhcp", "discover-uefi-x64-pxeclient"), s_relay),
            ProxyDhcpListenPort.Dhcp,
            new DatagramArrival(ServedIndex, s_servedAddress, new IPEndPoint(s_relay, 67)),
            new byte[1500]);

        Assert.Equal(ProxyDhcpOutcomeKind.Silenced, outcome.Kind);
        Assert.Equal(ProxyDhcpSilenceReason.RelayNotAuthorised, outcome.SilenceReason);
    }

    // The address the firmware asks the boot server from is the one GRUB will load from a moment later.
    [Fact]
    public void ARequestOnTheBootServerPortResetsWhatTheMachinesStartBeforeLeftOpen()
    {
        BootConnections connections = new(TimeProvider.System);
        IPAddress machine = IPAddress.Parse("192.0.2.55");
        int reset = 0;
        using IDisposable left = connections.Opened(machine, () => reset++);
        using IDisposable other = connections.Opened(IPAddress.Parse("192.0.2.56"), () => Assert.Fail("Another machine's connection was reset."));

        ProxyDhcpOutcome outcome = Handler(connections: connections).Handle(
            Loopback.Fixture("Dhcp", "request-4011-uefi-x64"),
            ProxyDhcpListenPort.PxeBootServer,
            new DatagramArrival(ServedIndex, s_servedAddress, new IPEndPoint(machine, 4011)),
            new byte[1500]);

        Assert.Equal(ProxyDhcpOutcomeKind.Replied, outcome.Kind);
        Assert.Equal(1, outcome.ConnectionsReset);
        Assert.Equal(1, reset);
        Assert.False(connections.HttpIsSafe(machine));
    }

    // One datagram from anywhere must not end another machine's downloads: only the sender's own are reset.
    [Fact]
    public void ARequestThatNamesAnotherMachinesAddressInsideResetsNothingOfThatMachine()
    {
        BootConnections connections = new(TimeProvider.System);
        IPAddress sender = IPAddress.Parse("192.0.2.77");
        IPAddress victim = IPAddress.Parse("192.0.2.55");
        int reset = 0;
        using IDisposable own = connections.Opened(sender, () => reset++);
        using IDisposable downloading = connections.Opened(victim, () => Assert.Fail("A machine named inside the packet was reset."));

        ProxyDhcpOutcome outcome = Handler(connections: connections).Handle(
            WithClientAddress(Loopback.Fixture("Dhcp", "request-4011-uefi-x64"), victim),
            ProxyDhcpListenPort.PxeBootServer,
            new DatagramArrival(ServedIndex, s_servedAddress, new IPEndPoint(sender, 4011)),
            new byte[1500]);

        Assert.Equal(ProxyDhcpOutcomeKind.Replied, outcome.Kind);
        Assert.Equal(1, outcome.ConnectionsReset);
        Assert.Equal(1, reset);
        Assert.True(connections.HttpIsSafe(victim));
        Assert.Equal(1, connections.Open);
    }

    // Behind a relay the sender is the relay, so neither it nor the address the packet names proves a machine.
    [Fact]
    public void ARelayedRequestOnTheBootServerPortResetsNothing()
    {
        BootConnections connections = new(TimeProvider.System);
        IPAddress victim = IPAddress.Parse("192.0.2.55");
        using IDisposable downloading = connections.Opened(victim, () => Assert.Fail("A machine behind a relay was reset."));
        using IDisposable relay = connections.Opened(s_relay, () => Assert.Fail("The relay's own connection was reset."));

        ProxyDhcpOutcome outcome = Handler(relays: "192.0.2.1", connections: connections).Handle(
            WithGateway(WithClientAddress(Loopback.Fixture("Dhcp", "request-4011-uefi-x64"), victim), s_relay),
            ProxyDhcpListenPort.PxeBootServer,
            new DatagramArrival(ServedIndex, s_servedAddress, new IPEndPoint(s_relay, 4011)),
            new byte[1500]);

        Assert.Equal(ProxyDhcpOutcomeKind.Replied, outcome.Kind);
        Assert.Equal(0, outcome.ConnectionsReset);
        Assert.True(connections.HttpIsSafe(victim));
        Assert.Equal(2, connections.Open);
    }

    // A DISCOVER comes from no address yet, so it names no machine whose connections could be reset.
    [Fact]
    public void ABroadcastDiscoverResetsNothing()
    {
        BootConnections connections = new(TimeProvider.System);
        using IDisposable open = connections.Opened(IPAddress.Any, () => Assert.Fail("A connection was reset for a DISCOVER."));
        using IDisposable served = connections.Opened(s_servedAddress, () => Assert.Fail("A connection was reset for a DISCOVER."));

        ProxyDhcpOutcome outcome = Handler(connections: connections).Handle(
            Loopback.Fixture("Dhcp", "discover-uefi-x64-pxeclient"),
            ProxyDhcpListenPort.Dhcp,
            new DatagramArrival(ServedIndex, IPAddress.Broadcast, new IPEndPoint(IPAddress.Any, 68)),
            new byte[1500]);

        Assert.Equal(ProxyDhcpOutcomeKind.Replied, outcome.Kind);
        Assert.Equal(0, outcome.ConnectionsReset);
        Assert.Equal(2, connections.Open);
    }

    [Fact]
    public void ReportsAnUnparseableDatagram()
    {
        ProxyDhcpOutcome outcome = Handler().Handle(
            Loopback.Fixture("Dhcp", "discover-option-length-overruns"),
            ProxyDhcpListenPort.Dhcp,
            new DatagramArrival(ServedIndex, IPAddress.Broadcast, new IPEndPoint(IPAddress.Any, 68)),
            new byte[1500]);

        Assert.Equal(ProxyDhcpOutcomeKind.Unparseable, outcome.Kind);
        Assert.Equal(DhcpParseError.OptionOverrunsDatagram, outcome.ParseError);
    }
}
