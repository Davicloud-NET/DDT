// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Protocols.Dhcp;
using DDT.Protocols.Pxe;

namespace DDT.Pxe;

// Everything between a received datagram and the bytes to send, without the socket. The listener
// passes in what IP_PKTINFO reported, which is what makes the addressing rules testable.
public sealed class ProxyDhcpHandler
{
    private readonly ProxyDhcpConfiguration _configuration;
    private readonly NetworkInterfaceMap _interfaces;
    private readonly BootConnections? _connections;
    private readonly NetbootChoice? _choice;

    // Without a choice every machine gets the boot targets of the configuration.
    public ProxyDhcpHandler(
        ProxyDhcpConfiguration configuration,
        NetworkInterfaceMap interfaces,
        BootConnections? connections = null,
        NetbootChoice? choice = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(interfaces);

        _configuration = configuration;
        _interfaces = interfaces;
        _connections = connections;
        _choice = choice;
    }

    public ProxyDhcpOutcome Handle(ReadOnlySpan<byte> datagram, ProxyDhcpListenPort port, DatagramArrival arrival, Span<byte> reply)
    {
        ArgumentNullException.ThrowIfNull(arrival);
        ArgumentNullException.ThrowIfNull(arrival.Address);
        ArgumentNullException.ThrowIfNull(arrival.Source);

        if (!_interfaces.TryGetInterface(arrival.Interface, out ServedInterface? served))
        {
            return new ProxyDhcpOutcome { Kind = ProxyDhcpOutcomeKind.InterfaceNotServed };
        }

        if (!DhcpMessageParser.TryParse(datagram, out DhcpMessage? message, out DhcpParseError error))
        {
            return new ProxyDhcpOutcome { Kind = ProxyDhcpOutcomeKind.Unparseable, ParseError = error };
        }

        // A broadcast DISCOVER says nothing about which of our addresses the client can reach, so the interface's
        // first address answers it. A request on 4011 or from a relay was sent to one of our addresses, and option 54
        // must carry that one.
        IPAddress localAddress = served.Owns(arrival.Address) ? arrival.Address : served.Address;

        ProxyDhcpDecision decision = Decide(new ProxyDhcpRequest
        {
            Message = message,
            ReceivedOn = port,
            LocalAddress = localAddress,
            SourceAddress = arrival.Source.Address,
            SourcePort = arrival.Source.Port,
        });

        if (!decision.TryGetReply(out ProxyDhcpReply? proxyReply))
        {
            return new ProxyDhcpOutcome
            {
                Kind = ProxyDhcpOutcomeKind.Silenced,
                Message = message,
                SilenceReason = decision.SilenceReason,
            };
        }

        if (!ProxyDhcpReplyWriter.TryWrite(proxyReply, reply, out int length))
        {
            return new ProxyDhcpOutcome { Kind = ProxyDhcpOutcomeKind.ReplyTooLarge, Message = message, Reply = proxyReply };
        }

        return new ProxyDhcpOutcome
        {
            Kind = ProxyDhcpOutcomeKind.Replied,
            Message = message,
            Reply = proxyReply,
            Length = length,
            EgressInterface = proxyReply.Destination.Address.Equals(IPAddress.Broadcast) ? served.Index : 0,
            ConnectionsReset = Starting(message, port, arrival),
        };
    }

    // The machine's boot environment decides which boot file the answer names. A machine the server can start none
    // for gets no answer, but only where it would have got one, so every other reason to stay silent keeps its name.
    private ProxyDhcpDecision Decide(ProxyDhcpRequest request)
    {
        ProxyDhcpConfiguration? chosen = _choice is null ? _configuration : _choice.For(request.Message);
        ProxyDhcpDecision decision = ProxyDhcpResponder.Respond(request, chosen ?? _configuration);

        return chosen is null && decision.Reply is not null
            ? ProxyDhcpDecision.Silent(ProxyDhcpSilenceReason.NoBootEnvironmentForMachine)
            : decision;
    }

    // A request on the boot server port begins a machine's start, from the address it will load from. Only the
    // datagram's source counts: an address named inside the packet would let any host reset another machine's reads.
    private int Starting(DhcpMessage message, ProxyDhcpListenPort port, DatagramArrival arrival)
    {
        // Behind a relay the source is the relay, and nothing proves the machine's own address.
        if (_connections is null || port != ProxyDhcpListenPort.PxeBootServer || !message.GatewayAddress.Equals(IPAddress.Any))
        {
            return 0;
        }

        return arrival.Source.Address.Equals(IPAddress.Any) ? 0 : _connections.NetbootStarts(arrival.Source.Address);
    }
}
