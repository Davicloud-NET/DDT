// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Core.Windows;

// This process's standard input, output and error
public static partial class StandardHandles
{
    private const uint Inherit = 0x00000001;

    private static readonly int[] s_all = [-10, -11, -12];

    // A child that inherits a pipe keeps it open, and whoever reads it waits for the child too. False if Windows refused.
    public static bool KeepFromChildren()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        foreach (int which in s_all)
        {
            nint handle = GetStdHandle(which);

            if (handle is not (0 or -1) && !SetHandleInformation(handle, Inherit, 0))
            {
                return false;
            }
        }

        return true;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GetStdHandle(int which);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetHandleInformation(nint handle, uint mask, uint flags);
}
