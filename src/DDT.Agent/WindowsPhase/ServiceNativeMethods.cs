// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.WindowsPhase;

// The service control manager's functions in advapi32, with blittable arguments only, so nothing is marshalled.
internal static unsafe partial class ServiceNativeMethods
{
    public const int ErrorFailedServiceControllerConnect = 1063;

    [LibraryImport("advapi32.dll", EntryPoint = "StartServiceCtrlDispatcherW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool StartServiceCtrlDispatcher(ServiceTableEntry* serviceTable);

    [LibraryImport("advapi32.dll", EntryPoint = "RegisterServiceCtrlHandlerExW", SetLastError = true)]
    public static partial nint RegisterServiceCtrlHandlerEx(
        char* serviceName,
        delegate* unmanaged<uint, uint, void*, void*, uint> handler,
        void* context);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetServiceStatus(nint statusHandle, ServiceStatus* status);
}
