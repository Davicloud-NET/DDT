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

    public ProxyDhcpHandler(ProxyDhcpConfiguration configuration, NetworkInterfaceMap interfaces)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(interfaces);

        _configuration = configuration;
        _interfaces = interfaces;
    }

    public ProxyDhcpOutcome Handle(
        ReadOnlySpan<byte> datagram,
        ProxyDhcpListenPort port,
        int arrivalInterface,
        IPAddress arrivalAddress,
        IPEndPoint source,
        Span<byte> reply)
    {
        ArgumentNullException.ThrowIfNull(arrivalAddress);
        ArgumentNullException.ThrowIfNull(source);

        if (!_interfaces.TryGetInterface(arrivalInterface, out ServedInterface? served))
        {
            return new ProxyDhcpOutcome { Kind = ProxyDhcpOutcomeKind.InterfaceNotServed };
        }

        if (!DhcpMessageParser.TryParse(datagram, out DhcpMessage? message, out DhcpParseError error))
        {
            return new ProxyDhcpOutcome { Kind = ProxyDhcpOutcomeKind.Unparseable, ParseError = error };
        }

        // A broadcast DISCOVER arrives addressed to 255.255.255.255, which says nothing about which of our
        // addresses the client can reach, so the interface's first address answers it. A request on 4011
        // or from a relay was sent to one of our addresses, and that is the one expected in option 54.
        IPAddress localAddress = served.Owns(arrivalAddress) ? arrivalAddress : served.Address;

        ProxyDhcpDecision decision = ProxyDhcpResponder.Respond(
            new ProxyDhcpRequest
            {
                Message = message,
                ReceivedOn = port,
                LocalAddress = localAddress,
                SourceAddress = source.Address,
                SourcePort = source.Port,
            },
            _configuration);

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
        };
    }
}
