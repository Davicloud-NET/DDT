using System.Runtime.InteropServices;

namespace DDT.Core.Wim;

// The fields DDT reads of wimlib's struct wimlib_progress_info_extract (64-bit layout).
[StructLayout(LayoutKind.Explicit)]
internal readonly struct ExtractProgressInfo
{
    [FieldOffset(40)]
    internal readonly ulong TotalBytes;

    [FieldOffset(48)]
    internal readonly ulong CompletedBytes;
}
