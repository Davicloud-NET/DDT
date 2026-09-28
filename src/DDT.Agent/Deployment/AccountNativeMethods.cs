// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.Deployment;

// Signs an account in, reads its token, profile and environment, and starts a process as it on a separate window
// station and desktop, inside a job object. The installed Windows has all of these, and only the agent there runs
// tools as an account. The imports keep Win32's signatures, however many parameters they take.
internal static unsafe partial class AccountNativeMethods
{
    public const int LogonInteractive = 2;
    public const int LogonProviderDefault = 0;

    public const int TokenGroups = 2;
    public const int TokenElevationType = 18;
    public const int TokenLinkedToken = 19;
    public const int TokenElevationTypeLimited = 3;
    public const uint GroupLogonId = 0xC0000000;

    public const uint ProfileNoUi = 1;

    public const uint SddlRevision = 1;

    // WINSTA_ALL_ACCESS and DESKTOP's eight rights, each with DELETE, READ_CONTROL, WRITE_DAC and WRITE_OWNER.
    public const uint WindowStationAllAccess = 0xF037F;
    public const uint DesktopAllAccess = 0xF01FF;

    public const uint HandleFlagInherit = 0x1;

    public const uint CreateSuspended = 0x4;
    public const uint CreateUnicodeEnvironment = 0x400;
    public const uint CreateNoWindow = 0x08000000;
    public const uint ExtendedStartupInfoPresent = 0x80000;
    public const uint StartfUseStdHandles = 0x100;

    public const nuint ProcThreadAttributeHandleList = 0x20002;

    public const int JobObjectExtendedLimitInformation = 9;
    public const uint JobObjectLimitKillOnJobClose = 0x2000;

    public const uint ResumeFailed = uint.MaxValue;

    [StructLayout(LayoutKind.Sequential)]
    public struct SecurityAttributes
    {
        public uint Length;
        public nint SecurityDescriptor;
        public int InheritHandle;
    }

    // SID_AND_ATTRIBUTES, as TOKEN_GROUPS holds them after its count.
    [StructLayout(LayoutKind.Sequential)]
    public struct SidAndAttributes
    {
        public nint Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TokenGroupsHeader
    {
        public uint GroupCount;
        public SidAndAttributes First;
    }

    // PROFILEINFOW.
    [StructLayout(LayoutKind.Sequential)]
    public struct ProfileInfo
    {
        public uint Size;
        public uint Flags;
        public char* UserName;
        public char* ProfilePath;
        public char* DefaultPath;
        public char* ServerName;
        public char* PolicyPath;
        public nint Profile;
    }

    // STARTUPINFOEXW.
    [StructLayout(LayoutKind.Sequential)]
    public struct StartupInfoEx
    {
        public uint Size;
        public char* Reserved;
        public char* Desktop;
        public char* Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountChars;
        public uint YCountChars;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2Size;
        public byte* Reserved2;
        public nint StandardInput;
        public nint StandardOutput;
        public nint StandardError;
        public void* AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    // JOBOBJECT_EXTENDED_LIMIT_INFORMATION, with its basic limits and I/O counters written out.
    [StructLayout(LayoutKind.Sequential)]
    public struct JobExtendedLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    // UTF-16 strings are pinned, not copied, so the password stays in a single managed string.
    [LibraryImport("advapi32.dll", EntryPoint = "LogonUserW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LogonUser(string userName, string? domain, string password, int logonType, int logonProvider, out SafeKernelHandle token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTokenInformation(SafeKernelHandle token, int informationClass, void* information, uint length, out uint returnedLength);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ImpersonateLoggedOnUser(SafeKernelHandle token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RevertToSelf();

    [LibraryImport(
        "advapi32.dll",
        EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out nint descriptor, out uint length);

    // The command line must be writable.
    [LibraryImport("advapi32.dll", EntryPoint = "CreateProcessAsUserW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateProcessAsUser(
        SafeKernelHandle token,
        string? applicationName,
        char* commandLine,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        char* environment,
        string? currentDirectory,
        StartupInfoEx* startupInfo,
        out ProcessInformation processInformation);

    [LibraryImport("userenv.dll", EntryPoint = "LoadUserProfileW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LoadUserProfile(SafeKernelHandle token, ProfileInfo* profile);

    [LibraryImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnloadUserProfile(SafeKernelHandle token, nint profile);

    [LibraryImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateEnvironmentBlock(out char* environment, SafeKernelHandle token, [MarshalAs(UnmanagedType.Bool)] bool inherit);

    [LibraryImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyEnvironmentBlock(char* environment);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowStationW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowStation(string name, uint flags, uint desiredAccess, SecurityAttributes* attributes);

    [LibraryImport("user32.dll", EntryPoint = "CreateDesktopW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateDesktop(string name, nint device, nint deviceMode, uint flags, uint desiredAccess, SecurityAttributes* attributes);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint GetProcessWindowStation();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessWindowStation(nint windowStation);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseWindowStation(nint windowStation);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseDesktop(nint desktop);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreatePipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe, SecurityAttributes* attributes, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetHandleInformation(SafeHandle handle, uint mask, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeKernelHandle CreateJobObject(nint attributes, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetInformationJobObject(SafeKernelHandle job, int informationClass, void* information, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AssignProcessToJobObject(SafeKernelHandle job, SafeKernelHandle process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateJobObject(SafeKernelHandle job, uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateProcess(SafeKernelHandle process, uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint ResumeThread(SafeKernelHandle thread);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetExitCodeProcess(SafeKernelHandle process, out uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InitializeProcThreadAttributeList(void* attributeList, int attributeCount, uint flags, ref nuint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UpdateProcThreadAttribute(
        void* attributeList,
        uint flags,
        nuint attribute,
        void* value,
        nuint size,
        void* previousValue,
        nuint* returnSize);

    [LibraryImport("kernel32.dll")]
    public static partial void DeleteProcThreadAttributeList(void* attributeList);

    [LibraryImport("kernel32.dll")]
    public static partial nint LocalFree(nint memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);
}
