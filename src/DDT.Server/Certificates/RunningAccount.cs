// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Principal;

namespace DDT.Server.Certificates;

// The account a message names when a file is someone else's. A Windows service's virtual account, NT SERVICE\DDT,
// has no user name of its own.
internal static class RunningAccount
{
    public static string Name => OperatingSystem.IsWindows() ? WindowsName() : Environment.UserName;

    // What to do about a file DDT may not read, after the system's own words
    public static string AccessAdvice => $"DDT runs as {Name}. Give that account read access to the file, which another account made.";

    private static string WindowsName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Environment.UserName;
        }

        using WindowsIdentity identity = WindowsIdentity.GetCurrent();

        return identity.Name;
    }
}
