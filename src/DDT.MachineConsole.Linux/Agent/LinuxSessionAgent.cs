// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Pipes;
using DDT.MachineConsole.Native;

namespace DDT.MachineConsole.Agent;

// In the installed Linux the agent runs as root and the console as a user of its own. The kernel says who is at the
// other end of the socket, so only root is trusted.
public static class LinuxSessionAgent
{
    // SOL_SOCKET and SO_PEERCRED; struct ucred is pid, uid, gid.
    private const int SocketLevel = 1;
    private const int PeerCredentials = 17;

    public static unsafe void OwnedByRoot(NamedPipeClientStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        int* credentials = stackalloc int[3];
        uint length = sizeof(int) * 3;
        int socket = (int)pipe.SafePipeHandle.DangerousGetHandle();

        if (Libc.GetSocketOption(socket, SocketLevel, PeerCredentials, credentials, &length) != 0 || credentials[1] != 0)
        {
            throw new UnauthorizedAccessException("The socket is not the agent's: root is not at its other end.");
        }
    }
}
