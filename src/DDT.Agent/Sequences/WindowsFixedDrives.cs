// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Sequences;

// Where WinPE looks for a run: the fixed drives that are ready, except its own X:.
public static class WindowsFixedDrives
{
    // Listed again each time it is read, so a Windows volume that got its letter after the agent started is found too.
    public static IEnumerable<string> Roots()
    {
        string? own = Path.GetPathRoot(Environment.SystemDirectory);

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType == DriveType.Fixed
                && drive.IsReady
                && !string.Equals(drive.RootDirectory.FullName, own, StringComparison.OrdinalIgnoreCase))
            {
                yield return drive.RootDirectory.FullName;
            }
        }
    }
}
