namespace DDT.Protocols.Tftp;

public readonly record struct TftpRequestedOptions(int? BlockSize, int? Timeout, long? TransferSize, int? WindowSize)
{
    public bool Any => BlockSize is not null || Timeout is not null || TransferSize is not null || WindowSize is not null;
}
