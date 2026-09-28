// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;
using DDT.Contracts.Machines;

namespace DDT.Agent;

// What the agent tells the server about the machine; a fact is null where the firmware or Windows does not tell.
// TrustedUefiCas are Microsoft's third-party UEFI CAs the firmware trusts; only the console shows the IpAddresses.
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
