// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Core.Windows;

// Whether a Windows service is installed and runs, and in which process. Read from the service manager's own list,
// which every account may enumerate. A service such as the DHCP server refuses a question put to itself.
public static partial class WindowsServices
{
    private const uint ManagerEnumerate = 0x0004;
    private const int ProcessInformation = 0;
    private const uint Win32Services = 0x00000030;
    private const uint AnyState = 0x00000003;
    private const int Running = 4;
    private const int MoreData = 234;

    // ENUM_SERVICE_STATUS_PROCESSW: two pointers, then SERVICE_STATUS_PROCESS, whose second DWORD is the state and
    // whose eighth is the process
    private static readonly int s_stateOffset = (2 * nint.Size) + 4;
    private static readonly int s_processOffset = (2 * nint.Size) + 28;
    private static readonly int s_entryBytes = (((2 * nint.Size) + 36 + nint.Size - 1) / nint.Size) * nint.Size;

    // All false and zero on another system, and for a service Windows does not have.
    public static (bool Installed, bool Running, int ProcessId) State(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (!OperatingSystem.IsWindows())
        {
            return default;
        }

        nint manager = OpenSCManager(null, null, ManagerEnumerate);

        if (manager == 0)
        {
            return default;
        }

        try
        {
            return Find(manager, name);
        }
        finally
        {
            _ = CloseServiceHandle(manager);
        }
    }

    private static (bool Installed, bool Running, int ProcessId) Find(nint manager, string name)
    {
        uint resume = 0;
        _ = EnumServicesStatusEx(manager, ProcessInformation, Win32Services, AnyState, 0, 0, out uint needed, out _, ref resume, null);

        if (Marshal.GetLastPInvokeError() != MoreData)
        {
            return default;
        }

        // A service can appear between the two calls
        int size = (int)needed + 4096;
        nint list = Marshal.AllocHGlobal(size);

        try
        {
            resume = 0;

            if (!EnumServicesStatusEx(manager, ProcessInformation, Win32Services, AnyState, list, (uint)size, out _, out uint count, ref resume, null))
            {
                return default;
            }

            for (int index = 0; index < count; index++)
            {
                nint entry = list + (index * s_entryBytes);

                if (string.Equals(Marshal.PtrToStringUni(Marshal.ReadIntPtr(entry)), name, StringComparison.OrdinalIgnoreCase))
                {
                    return (true, Marshal.ReadInt32(entry, s_stateOffset) == Running, Marshal.ReadInt32(entry, s_processOffset));
                }
            }

            return default;
        }
        finally
        {
            Marshal.FreeHGlobal(list);
        }
    }

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenSCManager(string? machine, string? database, uint access);

    [LibraryImport("advapi32.dll", EntryPoint = "EnumServicesStatusExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumServicesStatusEx(
        nint manager,
        int level,
        uint type,
        uint state,
        nint services,
        uint size,
        out uint needed,
        out uint returned,
        ref uint resume,
        string? group);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(nint handle);
}
