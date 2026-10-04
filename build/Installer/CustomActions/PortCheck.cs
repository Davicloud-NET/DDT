// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.NetworkInformation;
using WixToolset.Dtf.WindowsInstaller;

namespace DDT.Installer.CustomActions;

// PORTINUSE or PORTINVALID, so setup asks for another PORT.
public static class PortCheck
{
    [CustomAction]
    public static ActionResult CheckPort(Session session)
    {
        bool valid = int.TryParse(session["PORT"], out int port) && port is > 0 and < 65536;
        bool taken = valid && IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == port);

        session["PORTINVALID"] = valid ? string.Empty : "1";
        session["PORTINUSE"] = taken ? "1" : string.Empty;

        return ActionResult.Success;
    }
}
