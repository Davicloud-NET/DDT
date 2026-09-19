using System.Runtime.InteropServices;

namespace DDT.Agent.Deployment;

// The firmware variable functions of kernel32, and what enabling SeSystemEnvironmentPrivilege takes from advapi32.
internal static unsafe partial class FirmwareNativeMethods
{
    public const uint TokenAdjustPrivileges = 0x0020;
    public const uint TokenQuery = 0x0008;
    public const uint SePrivilegeEnabled = 0x00000002;
    public const string SeSystemEnvironmentName = "SeSystemEnvironmentPrivilege";

    public const int ErrorSuccess = 0;
    public const int ErrorInvalidFunction = 1;
    public const int ErrorEnvVarNotFound = 203;
    public const int ErrorNotAllAssigned = 1300;

    [LibraryImport("kernel32.dll", EntryPoint = "GetFirmwareEnvironmentVariableExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint GetFirmwareEnvironmentVariableEx(string name, string guid, byte* buffer, uint size, nint attributes);

    [LibraryImport("kernel32.dll", EntryPoint = "SetFirmwareEnvironmentVariableExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetFirmwareEnvironmentVariableEx(string name, string guid, byte* value, uint size, uint attributes);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint process, uint desiredAccess, out nint token);

    // A LUID is a 64-bit little-endian value: LowPart, then HighPart.
    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LookupPrivilegeValue(string? systemName, string name, out long luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AdjustTokenPrivileges(
        nint token,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        byte* newState,
        uint bufferLength,
        byte* previousState,
        uint* returnLength);
}
