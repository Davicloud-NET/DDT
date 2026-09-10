namespace DDT.Protocols.Tftp;

public enum TftpOpcode : ushort
{
    ReadRequest = 1,
    WriteRequest = 2,
    Data = 3,
    Acknowledgement = 4,
    Error = 5,
    OptionAcknowledgement = 6,
}
