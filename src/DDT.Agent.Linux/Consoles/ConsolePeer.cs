// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Pipes;

namespace DDT.Agent.Consoles;

// Who connected to the console's socket. Only a process of the console's unit is let in: no other user can start one
// there, and the kernel names the process.
internal static class ConsolePeer
{
    private const string UnitGroup = ":/system.slice/" + LinuxConsoleUnit.Name;

    // Why the process at the other end is not the console, or null.
    public static unsafe string? Refusal(NamedPipeServerStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        int* credentials = stackalloc int[3];
        uint length = sizeof(int) * 3;
        int socket = (int)pipe.SafePipeHandle.DangerousGetHandle();

        if (ConsoleNativeMethods.GetSocketOption(socket, ConsoleNativeMethods.SocketLevel, ConsoleNativeMethods.PeerCredentials, credentials, &length) != 0)
        {
            return "Who connected to the console's socket cannot be told.";
        }

        int process = credentials[0];

        return InUnit(Groups(process)) ? null : $"Process {process} connected to the console's socket, but it is not the console's.";
    }

    // The lines of /proc/<pid>/cgroup: 0::/system.slice/… with cgroup v2, and one per hierarchy with v1.
    public static bool InUnit(IEnumerable<string> lines) => lines.Any(line => line.EndsWith(UnitGroup, StringComparison.Ordinal));

    private static string[] Groups(int process)
    {
        try
        {
            return File.ReadAllLines($"/proc/{process}/cgroup");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
