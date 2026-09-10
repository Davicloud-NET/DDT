namespace DDT.Protocols.Tftp;

// Block is the value that goes on the wire, already truncated to 16 bits. FileOffset and Length say
// which bytes the caller must read, so the session never touches the file.
public sealed record TftpSendData(ushort Block, long FileOffset, int Length) : TftpAction;
