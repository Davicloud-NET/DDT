namespace DDT.Protocols.Tftp;

public sealed record TftpReadRequest(string FileName, string Mode, TftpRequestedOptions Options);
