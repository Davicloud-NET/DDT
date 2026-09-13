using DDT.Protocols.Dhcp;
using DDT.Protocols.Pxe;

namespace DDT.Pxe;

public sealed record ProxyDhcpOutcome
{
    public required ProxyDhcpOutcomeKind Kind { get; init; }

    public DhcpMessage? Message { get; init; }

    public DhcpParseError ParseError { get; init; }

    public ProxyDhcpSilenceReason SilenceReason { get; init; }

    public ProxyDhcpReply? Reply { get; init; }

    public int Length { get; init; }

    // Zero leaves the choice of interface to the routing table.
    public int EgressInterface { get; init; }
}
