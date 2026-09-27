// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.WindowsPhase;

// Local accounts from netapi32, the auto-logon secret from LSA in advapi32, profiles from userenv and sessions from
// wtsapi32, for DDT's session in the installed Windows.
internal static unsafe partial class SessionNativeMethods
{
    public const uint UserPrivilegeUser = 1;

    public const uint UfScript = 0x0001;
    public const uint UfAccountDisable = 0x0002;
    public const uint UfPasswordCannotChange = 0x0040;
    public const uint UfDontExpirePassword = 0x10000;

    public const int NerrSuccess = 0;
    public const int NerrUserNotFound = 2221;
    public const int NerrUserExists = 2224;
    public const int ErrorFileNotFound = 2;
    public const int ErrorPathNotFound = 3;
    public const int ErrorMemberInAlias = 1378;

    // HRESULT_FROM_WIN32(ERROR_ALREADY_EXISTS), which CreateProfile returns for a profile that is there already.
    public const int ProfileExists = unchecked((int)0x800700B7);

    public const uint PolicyGetPrivateInformation = 0x00000004;
    public const uint PolicyCreateSecret = 0x00000020;
    public const uint StatusObjectNameNotFound = 0xC0000034;

    public const nint WtsCurrentServer = 0;
    public const int WtsUserName = 5;
    public const int WtsDomainName = 7;

    // USER_INFO_1.
    [StructLayout(LayoutKind.Sequential)]
    public struct UserInfo1
    {
        public char* Name;
        public char* Password;
        public uint PasswordAge;
        public uint Privilege;
        public char* HomeDirectory;
        public char* Comment;
        public uint Flags;
        public char* ScriptPath;
    }

    // LSA_UNICODE_STRING: the lengths are in bytes, without a terminating null.
    [StructLayout(LayoutKind.Sequential)]
    public struct LsaUnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public char* Buffer;
    }

    // LSA_OBJECT_ATTRIBUTES, which LsaOpenPolicy wants zeroed but for its length.
    [StructLayout(LayoutKind.Sequential)]
    public struct LsaObjectAttributes
    {
        public uint Length;
        public nint RootDirectory;
        public LsaUnicodeString* ObjectName;
        public uint Attributes;
        public nint SecurityDescriptor;
        public nint SecurityQualityOfService;
    }

    // WTS_SESSION_INFOW.
    [StructLayout(LayoutKind.Sequential)]
    public struct WtsSessionInfo
    {
        public uint SessionId;
        public char* WinStationName;
        public int State;
    }

    [LibraryImport("netapi32.dll", EntryPoint = "NetUserAdd", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int NetUserAdd(string? server, uint level, void* buffer, out uint parameterError);

    [LibraryImport("netapi32.dll", EntryPoint = "NetUserSetInfo", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int NetUserSetInfo(string? server, string userName, uint level, void* buffer, out uint parameterError);

    [LibraryImport("netapi32.dll", EntryPoint = "NetUserDel", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int NetUserDel(string? server, string userName);

    // Level 0 takes LOCALGROUP_MEMBERS_INFO_0, a SID pointer per member.
    [LibraryImport("netapi32.dll", EntryPoint = "NetLocalGroupAddMembers", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int NetLocalGroupAddMembers(string? server, string groupName, uint level, void* buffer, uint count);

    // The LSA functions return an NTSTATUS.
    [LibraryImport("advapi32.dll")]
    public static partial uint LsaOpenPolicy(LsaUnicodeString* systemName, LsaObjectAttributes* attributes, uint access, out nint policy);

    // A null data deletes the secret.
    [LibraryImport("advapi32.dll")]
    public static partial uint LsaStorePrivateData(nint policy, LsaUnicodeString* key, LsaUnicodeString* data);

    [LibraryImport("advapi32.dll")]
    public static partial uint LsaRetrievePrivateData(nint policy, LsaUnicodeString* key, out LsaUnicodeString* data);

    [LibraryImport("advapi32.dll")]
    public static partial uint LsaFreeMemory(void* buffer);

    [LibraryImport("advapi32.dll")]
    public static partial uint LsaClose(nint policy);

    [LibraryImport("advapi32.dll")]
    public static partial int LsaNtStatusToWinError(uint status);

    // Returns an HRESULT.
    [LibraryImport("userenv.dll", EntryPoint = "CreateProfile", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int CreateProfile(string sid, string userName, char* profilePath, uint length);

    [LibraryImport("userenv.dll", EntryPoint = "DeleteProfileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteProfile(string sid, string? profilePath, string? computerName);

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSEnumerateSessionsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WtsEnumerateSessions(nint server, uint reserved, uint version, out WtsSessionInfo* sessions, out uint count);

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WtsQuerySessionInformation(nint server, uint sessionId, int infoClass, out char* buffer, out uint bytes);

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSFreeMemory")]
    public static partial void WtsFreeMemory(void* memory);

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSLogoffSession", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WtsLogoffSession(nint server, uint sessionId, [MarshalAs(UnmanagedType.Bool)] bool wait);
}
