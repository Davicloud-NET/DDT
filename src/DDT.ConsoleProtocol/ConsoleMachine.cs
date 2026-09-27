// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// What the agent knows about the machine, for a side panel, empty until it has read the machine. MacAddresses start with
// the primary one, the adapter with the default route, each as 12 hexadecimal digits. IpAddresses are that adapter's
// IPv4 addresses. SecureBootEnabled and TrustedUefiCas are null where the firmware does not say. Disks are the disks DDT
// can install on, null until they are read. KeyboardLayout is the layout the boot image set, which every typed
// password depends on.
public sealed record ConsoleMachine(
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? SmbiosUuid,
    IReadOnlyList<string> MacAddresses,
    IReadOnlyList<string> IpAddresses,
    bool? SecureBootEnabled,
    MicrosoftUefiCas? TrustedUefiCas,
    IReadOnlyList<ConsoleDisk>? Disks,
    string? KeyboardLayout);
