// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// The pxe section.
public sealed record PxeSettings(
    IReadOnlyList<string> Interfaces,
    bool EnableProxyDhcp,
    bool EnableTftp,
    bool TftpSinglePort,
    int TftpMaxWindowSize,
    int MaxConcurrentTftpTransfers,
    IReadOnlyList<string> AuthorisedRelayAgents,
    // Keyed by client architecture, such as X64Uefi.
    IReadOnlyDictionary<string, BootTargetSettings> BootTargets,
    // Leaves UDP 67 to a DHCP server on the same computer, so DDT answers on UDP 4011 alone.
    bool LeaveDhcpPort = false,
    // What a machine without a run starts: Auto, WindowsPE or Linux. Null means Auto.
    string? DefaultBootEnvironment = null,
    // Words added to the kernel command line of the Linux boot environment.
    string? LinuxKernelArguments = null);
