// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// What the SMBIOS table says of the machine, from the first structure of each type. A member is null where there is no
// such structure, it is too old to have the field, or the firmware left it empty. ChassisType is without the lock bit.
public sealed record SmbiosSystemInformation(Guid Uuid, string? Manufacturer, string? ProductName, string? SerialNumber, byte? ChassisType)
{
    // The System Information structure's (type 1) version, where Lenovo puts the name a person knows the model by, and its
    // SKU and family, which SMBIOS 2.4 added.
    public string? Version { get; init; }

    public string? Sku { get; init; }

    public string? Family { get; init; }

    // The BIOS Information structure's (type 0) version and release date, the date as yyyy-MM-dd.
    public string? BiosVersion { get; init; }

    public string? BiosDate { get; init; }

    // The Baseboard Information structure's (type 2) product.
    public string? BaseboardProduct { get; init; }

    // The System Enclosure structure's (type 3) asset tag.
    public string? AssetTag { get; init; }

    // The version of the first Processor Information structure (type 4) whose socket holds a processor, such as
    // "Intel(R) Core(TM) i7-1365U".
    public string? ProcessorVersion { get; init; }
}
