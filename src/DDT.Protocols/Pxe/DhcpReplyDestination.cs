using System.Net;

namespace DDT.Protocols.Pxe;

public readonly record struct DhcpReplyDestination(IPAddress Address, int Port);
