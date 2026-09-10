namespace DDT.Protocols.Tftp;

public readonly record struct TftpLimits(
    int MaxBlockSize,
    int MaxWindowSize,
    int MaxRetries,
    TimeSpan DefaultTimeout,
    TimeSpan MaxRetransmitDelay)
{
    // 1400 rather than 1456 because DDT sites span VPNs, where tunnel path MTU is typically 1380 to
    // 1420 and an oversized block silently fragments or is dropped. Four is a deliberately modest
    // window: firmware that requests a larger one frequently mishandles it.
    //
    // Three retries at a doubling delay capped at four seconds abandons a silent client eleven
    // seconds after the last acknowledgement. That has to land clearly inside the fifteen to twenty
    // four seconds EDK2 waits before giving up on the server, so DDT frees the session first rather
    // than holding it open for a machine that has already moved on.
    public static TftpLimits Default => new(1400, 4, 3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4));
}
