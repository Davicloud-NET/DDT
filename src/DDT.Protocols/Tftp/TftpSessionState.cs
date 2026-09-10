namespace DDT.Protocols.Tftp;

public enum TftpSessionState
{
    AwaitingOptionAck,
    Transferring,
    Completed,
    Failed,
}
