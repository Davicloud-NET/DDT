using System.Runtime.InteropServices;

namespace DDT.Core.Wim;

// The fields DDT reads of wimlib's struct wimlib_progress_info_write_streams (64-bit layout).
[StructLayout(LayoutKind.Explicit)]
internal readonly struct WriteStreamsProgressInfo
{
    [FieldOffset(0)]
    internal readonly ulong TotalBytes;

    [FieldOffset(16)]
    internal readonly ulong CompletedBytes;
}
