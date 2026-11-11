// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace DDT.Agent.Consoles;

// The pipe of the console in DDT's session.
[SupportedOSPlatform("windows")]
public static class SessionPipe
{
    // Only the agent's account, SYSTEM, and the session's account may open it. The agent's account owns it, and the
    // console checks that. The agent keeps its single instance between consoles, so the name never becomes free for
    // another process.
    public static NamedPipeServerStream Create(string name, SecurityIdentifier console)
    {
        ArgumentNullException.ThrowIfNull(console);

        SecurityIdentifier agent = WindowsIdentity.GetCurrent().User!;
        PipeSecurity security = new();
        security.SetOwner(agent);
        security.AddAccessRule(new PipeAccessRule(agent, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(console, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            name,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
            0,
            0,
            security);
    }
}
