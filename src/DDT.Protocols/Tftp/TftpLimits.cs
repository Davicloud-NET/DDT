namespace DDT.Protocols.Tftp;

public readonly record struct TftpLimits(int MaxBlockSize, int MaxWindowSize, int MaxRetries, TimeSpan DefaultTimeout)
{
    // 1400 rather than 1456 because DDT sites span VPNs, where tunnel path MTU is typically 1380 to
    // 1420 and an oversized block silently fragments or is dropped. Four is a deliberately modest
    // window: firmware that requests a larger one frequently mishandles it.
    public static TftpLimits Default => new(1400, 4, 5, TimeSpan.FromSeconds(1));
}
