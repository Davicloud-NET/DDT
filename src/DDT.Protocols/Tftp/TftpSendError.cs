namespace DDT.Protocols.Tftp;

public sealed record TftpSendError(TftpErrorCode Code, string Message) : TftpAction;
