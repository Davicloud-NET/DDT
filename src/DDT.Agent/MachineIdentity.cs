// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;

namespace DDT.Agent;

// SecureBootEnabled is what the firmware says, null when Windows does not tell. TrustedUefiCas says which of Microsoft's
// third-party UEFI CAs the firmware trusts, null when its signature database cannot be read. ChassisType is the SMBIOS
// chassis type, null when the firmware lists no enclosure.
public sealed record MachineIdentity(
    string SmbiosUuid,
    string PrimaryMac,
    IReadOnlyList<string> MacAddresses,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    bool? SecureBootEnabled = null,
    UefiCa? TrustedUefiCas = null,
    byte? ChassisType = null);
