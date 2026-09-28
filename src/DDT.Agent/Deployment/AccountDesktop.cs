// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.Versioning;
using static DDT.Agent.Deployment.AccountNativeMethods;

namespace DDT.Agent.Deployment;

// A window station and desktop of their own for a tool started as an account, open to SYSTEM and the logon SID only.
// Without them an interactive process fails to start with 0xC0000142.
[SupportedOSPlatform("windows")]
internal sealed class AccountDesktop : IDisposable
{
    private const string DesktopName = "default";

    // Setting the process's window station is process-wide, so only one start does it at a time.
    private static readonly Lock s_windowStationLock = new();

    private readonly nint _windowStation;
    private readonly nint _desktop;

    private AccountDesktop(nint windowStation, nint desktop, string name)
    {
        _windowStation = windowStation;
        _desktop = desktop;
        Name = name;
    }

    // As STARTUPINFO's lpDesktop takes it: station\desktop.
    public string Name { get; }

    public static unsafe AccountDesktop Create(string logonSid)
    {
        string stationName = $"ddt-{Guid.NewGuid():N}";
        nint stationDescriptor = SecurityDescriptor($"D:(A;;0x{WindowStationAllAccess:X};;;SY)(A;;0x{WindowStationAllAccess:X};;;{logonSid})");
        nint desktopDescriptor = SecurityDescriptor($"D:(A;;0x{DesktopAllAccess:X};;;SY)(A;;0x{DesktopAllAccess:X};;;{logonSid})");

        try
        {
            SecurityAttributes stationSecurity = new() { Length = (uint)sizeof(SecurityAttributes), SecurityDescriptor = stationDescriptor };
            nint station = CreateWindowStation(stationName, 0, WindowStationAllAccess, &stationSecurity);

            if (station == 0)
            {
                throw DeploymentStepException.ForLastWin32Error("A window station for the script could not be created");
            }

            try
            {
                nint desktop = DesktopOn(station, desktopDescriptor);

                return new AccountDesktop(station, desktop, $@"{stationName}\{DesktopName}");
            }
            catch
            {
                _ = CloseWindowStation(station);

                throw;
            }
        }
        finally
        {
            if (stationDescriptor != 0)
            {
                _ = LocalFree(stationDescriptor);
            }

            if (desktopDescriptor != 0)
            {
                _ = LocalFree(desktopDescriptor);
            }
        }
    }

    public void Dispose()
    {
        if (_desktop != 0)
        {
            _ = CloseDesktop(_desktop);
        }

        if (_windowStation != 0)
        {
            _ = CloseWindowStation(_windowStation);
        }
    }

    // CreateDesktop creates on the process's own window station, so the station is swapped in around the call.
    private static unsafe nint DesktopOn(nint station, nint descriptor)
    {
        SecurityAttributes desktopSecurity = new() { Length = (uint)sizeof(SecurityAttributes), SecurityDescriptor = descriptor };

        lock (s_windowStationLock)
        {
            nint previous = GetProcessWindowStation();

            if (!SetProcessWindowStation(station))
            {
                throw DeploymentStepException.ForLastWin32Error("The script's window station could not be entered");
            }

            try
            {
                nint desktop = CreateDesktop(DesktopName, 0, 0, 0, DesktopAllAccess, &desktopSecurity);

                if (desktop == 0)
                {
                    throw DeploymentStepException.ForLastWin32Error("A desktop for the script could not be created");
                }

                return desktop;
            }
            finally
            {
                _ = SetProcessWindowStation(previous);
            }
        }
    }

    private static nint SecurityDescriptor(string sddl)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, SddlRevision, out nint descriptor, out _))
        {
            throw DeploymentStepException.ForLastWin32Error("A security descriptor for the script could not be built");
        }

        return descriptor;
    }
}
