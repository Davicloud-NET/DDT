// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.LinuxPhase;

namespace DDT.Agent.Consoles;

// Whether the installed Linux can show the console: it draws on a virtual terminal's framebuffer, is built for glibc,
// and leaves the screen to a display manager.
internal static class LinuxConsoleScreen
{
    // Why there is no picture, or null.
    public static string? Problem(LinuxAgentPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (File.Exists(paths.At("/etc/systemd/system/display-manager.service")))
        {
            return "A display manager has this machine's screen";
        }

        if (!File.Exists(paths.At("/dev/fb0")))
        {
            return "This machine has no framebuffer, /dev/fb0";
        }

        if (!File.Exists(paths.At("/dev/tty0")))
        {
            return "This Linux has no virtual terminals";
        }

        return File.Exists(paths.At("/lib64/ld-linux-x86-64.so.2")) ? null : "This Linux has no glibc, which the console is built for";
    }
}
