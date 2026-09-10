namespace DDT.Protocols.Tftp;

public sealed record TftpArmRetransmit(DateTimeOffset Deadline) : TftpAction;
