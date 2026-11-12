// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.WindowsPhase;

internal static partial class BootNativeMethods
{
    // SystemBootEnvironmentInformation, which answers SYSTEM_BOOT_ENVIRONMENT_INFORMATION.
    private const int SystemBootEnvironmentInformation = 90;
    private const int InformationLength = 32;

    // The boot entry Windows started from, which bcdedit calls {current}.
    public static Guid BootEntry()
    {
        byte[] information = new byte[InformationLength];
        int status = NtQuerySystemInformation(SystemBootEnvironmentInformation, information, InformationLength, out _);

        return status == 0 ? new Guid(information.AsSpan(0, 16)) : throw new InvalidDataException($"Windows did not name the boot entry it started from (0x{status:X8}).");
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtQuerySystemInformation(int systemInformationClass, [Out] byte[] systemInformation, int systemInformationLength, out int returnLength);
}
