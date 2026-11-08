// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Pipes;
using System.Security.Principal;

namespace DDT.MachineConsole.Agent;

// In DDT's session the agent runs as SYSTEM. No program in the session can create a pipe that SYSTEM owns, so only such
// a pipe is trusted.
public static class WindowsSessionAgent
{
    public static void OwnedBySystem(NamedPipeClientStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        if (pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner
            || !owner.IsWellKnown(WellKnownSidType.LocalSystemSid))
        {
            throw new UnauthorizedAccessException("The pipe is not the agent's: SYSTEM does not own it.");
        }
    }
}
