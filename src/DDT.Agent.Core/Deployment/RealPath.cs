// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;

namespace DDT.Agent.Deployment;

// A path with every link in it followed, as the kernel itself names the place. /usr/local or /var/lib may be links.
internal static class RealPath
{
    // Null when Linux finds nothing there. Elsewhere the path as it is written, in full.
    public static string? Of(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            return Path.GetFullPath(path);
        }

        byte[] resolved = new byte[LinuxFileNativeMethods.MaxPath];

        if (LinuxFileNativeMethods.realpath(path, ref resolved[0]) == 0)
        {
            return null;
        }

        return Encoding.UTF8.GetString(resolved, 0, Math.Max(0, Array.IndexOf(resolved, (byte)0)));
    }

    // Whether both name the same place. False when either is not there.
    public static bool Same(string? first, string? second) =>
        first is not null && second is not null && Of(first) is { } real && string.Equals(real, Of(second), StringComparison.Ordinal);
}
