// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// What the agent knows about the machine, for a side panel; empty until it has read the machine.
public sealed record ConsoleMachine(
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? SmbiosUuid,
    // 12 hexadecimal digits each, the adapter with the default route first.
    IReadOnlyList<string> MacAddresses,
    // The IPv4 addresses of the adapter with the default route.
    IReadOnlyList<string> IpAddresses,
    // Null, like TrustedUefiCas, where the firmware does not say.
    bool? SecureBootEnabled,
    MicrosoftUefiCas? TrustedUefiCas,
    // The disks DDT can install on; null until they are read.
    IReadOnlyList<ConsoleDisk>? Disks,
    // The layout the boot image set, which every typed password depends on.
    string? KeyboardLayout);
