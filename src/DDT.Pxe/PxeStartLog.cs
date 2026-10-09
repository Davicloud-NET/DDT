// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Protocols.Dhcp;
using DDT.Protocols.Pxe;
using Microsoft.Extensions.Logging;

namespace DDT.Pxe;

// What the log says of a setup as its listeners start: the boot file of each kind of machine.
internal static class PxeStartLog
{
    // files is null for a host that knows of no Linux boot environment, which says nothing about one. windowsPE is
    // NetbootChoice's answer, so the log says what machines are named.
    public static void BootTargets(ILogger logger, PxeSetup setup, BootFiles? files, bool windowsPE)
    {
        // A site DHCP server that names DDT in options 66 and 67 needs no boot targets at all.
        if (setup.ProxyDhcp.BootTargets.Count == 0 && setup.Options.EnableProxyDhcp)
        {
            PxeLog.NoBootTargets(logger);
        }

        LinuxBootBundle? linux = setup.LinuxProxyDhcp is null ? null : files?.Linux;

        foreach (BootTarget target in setup.ProxyDhcp.BootTargets.Values)
        {
            PxeLog.BootTarget(logger, target.Architecture, target.Method, target.BootFile);

            if (target.Method == BootMethod.Tftp && target.ServerAddress is null && !setup.Files.TryResolve(target.BootFile, out _))
            {
                BootFileMissing(logger, setup, target, linux is not null);
            }
        }

        if (files is not null)
        {
            LinuxBoot(logger, setup, linux, windowsPE);
        }
    }

    // A missing boot manager is the commonest cause of "DHCP works, nothing boots", unless the shim is named instead.
    private static void BootFileMissing(ILogger logger, PxeSetup setup, BootTarget target, bool linux)
    {
        if (linux && NoBootImage(setup) && target.Architecture == ClientArchitecture.X64Uefi && setup.DefaultEnvironment != DefaultBootEnvironment.WindowsPE)
        {
            PxeLog.BootFileAwaitsBootImage(logger, target.Architecture, target.BootFile);
        }
        else
        {
            PxeLog.BootFileMissing(logger, target.Architecture, target.BootFile, setup.Files.Serving);
        }
    }

    // Which machine gets it is decided for each request. This says whether it is offered at all.
    private static void LinuxBoot(ILogger logger, PxeSetup setup, LinuxBootBundle? linux, bool windowsPE)
    {
        if (linux is null)
        {
            PxeLog.NoLinuxBoot(logger, setup.LinuxProxyDhcp is null ? "TFTP is off" : "this server has none that can be started");

            return;
        }

        PxeLog.LinuxBoot(logger, PxeSetup.LinuxBootFile, linux.Folder, setup.DefaultEnvironment switch
        {
            DefaultBootEnvironment.Linux => "it",
            DefaultBootEnvironment.WindowsPE => "Windows PE",
            _ when !windowsPE && NoBootImage(setup) => "it until a boot image is built",
            _ when !windowsPE => "it, since the file Windows PE's boot target names is not there",
            _ when NamesAnotherServer(setup) => "Windows PE, since its boot target names another server",
            _ => "Windows PE, since a boot image is there",
        });
    }

    // A build puts the default boot manager there. With it there, a missing file is a wrong name, not a missing build.
    private static bool NoBootImage(PxeSetup setup) => !setup.Files.TryResolve(PxeSetup.DefaultBootFile, out _);

    // DDT cannot look into another TFTP server, so no boot image of its own stands behind that Windows PE.
    private static bool NamesAnotherServer(PxeSetup setup) =>
        setup.ProxyDhcp.BootTargets.TryGetValue(ClientArchitecture.X64Uefi, out BootTarget? target) && target.ServerAddress is not null;
}
