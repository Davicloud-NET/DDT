namespace DDT.Protocols.Tftp;

public readonly record struct TftpStep(TftpSessionState State, IReadOnlyList<TftpAction> Actions)
{
    public static TftpStep Nothing(TftpSessionState state) => new(state, []);
}
