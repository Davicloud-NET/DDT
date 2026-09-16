using System.Runtime.InteropServices;

namespace DDT.Agent;

internal static partial class NativeMethods
{
    public const uint RawSmbiosProvider = 0x52534D42;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint GetSystemFirmwareTable(uint firmwareTableProviderSignature, uint firmwareTableId, byte[]? buffer, uint bufferSize);
}
