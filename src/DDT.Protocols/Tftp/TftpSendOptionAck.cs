namespace DDT.Protocols.Tftp;

public sealed record TftpSendOptionAck(TftpNegotiation Negotiated) : TftpAction;
