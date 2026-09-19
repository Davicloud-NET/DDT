namespace DDT.Core.Wim;

public enum WimCompression
{
    None,
    Xpress,
    Lzx,

    // Written as a solid resource, the form of Windows Setup ESD files.
    Lzms,
}
