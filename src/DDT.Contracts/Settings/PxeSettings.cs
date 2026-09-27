// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// The section pxe. BootTargets are keyed by client architecture, such as X64Uefi.
public sealed record PxeSettings(
    IReadOnlyList<string> Interfaces,
    bool EnableProxyDhcp,
    bool EnableTftp,
    bool TftpSinglePort,
    int TftpMaxWindowSize,
    int MaxConcurrentTftpTransfers,
    IReadOnlyList<string> AuthorisedRelayAgents,
    IReadOnlyDictionary<string, BootTargetSettings> BootTargets);

// Method is Tftp or Http.
public sealed record BootTargetSettings(
    string? Method,
    string? BootFile,
    string? ServerAddress,
    string? ServerHostName,
    bool AdvertiseBootServerDiscovery);
