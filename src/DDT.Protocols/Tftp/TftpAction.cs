namespace DDT.Protocols.Tftp;

// The session emits actions rather than performing them, so it owns no socket and no timer and can
// be driven end to end by a test with a fake clock.
public abstract record TftpAction
{
    private protected TftpAction()
    {
    }
}
