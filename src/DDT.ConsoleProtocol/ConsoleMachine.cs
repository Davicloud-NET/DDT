// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// What the agent knows about the machine, for a side panel. It's empty until the agent has read the machine.
public sealed record ConsoleMachine(
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? SmbiosUuid,
    // 12 hexadecimal digits each, the adapter with the default route first.
    IReadOnlyList<string> MacAddresses,
    // The IPv4 addresses of the adapter with the default route.
    IReadOnlyList<string> IpAddresses,
    // Null, like TrustedUefiCas, when the firmware doesn't say.
    bool? SecureBootEnabled,
    MicrosoftUefiCas? TrustedUefiCas,
    // The disks DDT can install on. Null until they're read.
    IReadOnlyList<ConsoleDisk>? Disks,
    // The keyboard layout the boot image set. Every typed password depends on it.
    string? KeyboardLayout);
