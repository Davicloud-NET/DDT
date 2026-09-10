using System.Diagnostics.CodeAnalysis;

namespace DDT.Protocols.Pxe;

public readonly record struct ProxyDhcpDecision
{
    private ProxyDhcpDecision(ProxyDhcpReply? reply, ProxyDhcpSilenceReason silenceReason)
    {
        Reply = reply;
        SilenceReason = silenceReason;
    }

    public ProxyDhcpReply? Reply { get; }

    public ProxyDhcpSilenceReason SilenceReason { get; }

    public static ProxyDhcpDecision Respond(ProxyDhcpReply reply) =>
        new(reply, ProxyDhcpSilenceReason.None);

    public static ProxyDhcpDecision Silent(ProxyDhcpSilenceReason silenceReason) =>
        new(null, silenceReason);

    public bool TryGetReply([NotNullWhen(true)] out ProxyDhcpReply? reply)
    {
        reply = Reply;

        return reply is not null;
    }
}
