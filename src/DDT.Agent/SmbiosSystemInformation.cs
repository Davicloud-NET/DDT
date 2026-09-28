// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// What the SMBIOS table says about the machine, from the first structure of each type. A member is null when there's
// no such structure, the structure is too old to have the field, or the firmware left it empty. ChassisType has the
// lock bit removed.
public sealed record SmbiosSystemInformation(Guid Uuid, string? Manufacturer, string? ProductName, string? SerialNumber, byte? ChassisType)
{
    // The version, SKU and family from the System Information structure (type 1). Lenovo puts the model's everyday name
    // in the version. SMBIOS 2.4 added the SKU and the family.
    public string? Version { get; init; }

    public string? Sku { get; init; }

    public string? Family { get; init; }

    // The version and release date from the BIOS Information structure (type 0). The date is yyyy-MM-dd.
    public string? BiosVersion { get; init; }

    public string? BiosDate { get; init; }

    // The product from the Baseboard Information structure (type 2).
    public string? BaseboardProduct { get; init; }

    // The asset tag from the System Enclosure structure (type 3).
    public string? AssetTag { get; init; }

    // The version of the first Processor Information structure (type 4) whose socket holds a processor, such as
    // "Intel(R) Core(TM) i7-1365U".
    public string? ProcessorVersion { get; init; }
}
