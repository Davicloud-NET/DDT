// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;
using DDT.Contracts.Machines;

namespace DDT.Agent;

// What the agent tells the server about the machine. A fact is null when the firmware or Windows doesn't provide it.
// TrustedUefiCas are the Microsoft third-party UEFI CAs that the firmware trusts. Only the console shows the
// IpAddresses.
public sealed record MachineIdentity(
    string SmbiosUuid,
    string PrimaryMac,
    IReadOnlyList<string> MacAddresses,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    bool? SecureBootEnabled = null,
    UefiCa? TrustedUefiCas = null,
    byte? ChassisType = null,
    IReadOnlyList<string>? IpAddresses = null,
    MachineFacts? Facts = null);
