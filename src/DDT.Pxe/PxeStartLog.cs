// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Protocols.Pxe;
using Microsoft.Extensions.Logging;

namespace DDT.Pxe;

// What the log says of a setup as its listeners start: the boot file of each kind of machine.
internal static class PxeStartLog
{
    // files is null for a host that knows of no Linux boot environment, which says nothing about one.
    public static void BootTargets(ILogger logger, PxeSetup setup, BootFiles? files)
    {
        // A site DHCP server that names DDT in options 66 and 67 needs no boot targets at all.
        if (setup.ProxyDhcp.BootTargets.Count == 0 && setup.Options.EnableProxyDhcp)
        {
            PxeLog.NoBootTargets(logger);
        }

        foreach (BootTarget target in setup.ProxyDhcp.BootTargets.Values)
        {
            PxeLog.BootTarget(logger, target.Architecture, target.Method, target.BootFile);

            // A missing boot manager is the commonest cause of "DHCP works, nothing boots".
            if (target.Method == BootMethod.Tftp && target.ServerAddress is null && !setup.Files.TryResolve(target.BootFile, out _))
            {
                PxeLog.BootFileMissing(logger, target.Architecture, target.BootFile, setup.Files.Serving);
            }
        }

        if (files is not null)
        {
            LinuxBoot(logger, setup, files);
        }
    }

    // Which machine gets it is decided for each request. This says whether it is offered at all.
    private static void LinuxBoot(ILogger logger, PxeSetup setup, BootFiles files)
    {
        if (setup.LinuxProxyDhcp is null || files.Linux is not { } bundle)
        {
            PxeLog.NoLinuxBoot(logger, setup.LinuxProxyDhcp is null ? "TFTP is off" : "this server has none that can be started");

            return;
        }

        PxeLog.LinuxBoot(logger, PxeSetup.LinuxBootFile, bundle.Folder, setup.DefaultEnvironment switch
        {
            DefaultBootEnvironment.Linux => "it",
            DefaultBootEnvironment.WindowsPE => "Windows PE",
            _ => "Windows PE once a boot image is built, and it until then",
        });
    }
}
