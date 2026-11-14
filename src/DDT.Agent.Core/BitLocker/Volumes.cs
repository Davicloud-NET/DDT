// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.BitLocker;

// Volumes as manage-bde names them: a drive letter and a colon.
public static class Volumes
{
    public static string System => DriveOf(Environment.SystemDirectory);

    public static string DriveOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string root = Path.GetPathRoot(path) is { Length: > 0 } found ? found : "C:\\";

        return root.TrimEnd('\\', '/');
    }
}
