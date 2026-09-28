// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Machines;

// What the agent found out about the machine besides its identity, for conditions and rules to test and for the
// machine's page. Every member is null where the agent could not tell. The network members are the primary adapter's,
// the one PrimaryMac names. SystemVersion, SystemFamily and SystemSku are the SMBIOS system's (type 1), AssetTag its
// enclosure's (type 3), BaseboardProduct the baseboard's (type 2), and BiosVersion and BiosDate the BIOS's (type 0),
// the date as yyyy-MM-dd. TpmVersion is 2.0 or 1.2.
public sealed record MachineFacts
{
    public long? MemoryMegabytes { get; init; }

    public string? ProcessorName { get; init; }

    public int? ProcessorCores { get; init; }

    public int? LogicalProcessors { get; init; }

    public bool? TpmPresent { get; init; }

    public string? TpmVersion { get; init; }

    public bool? SecureBootCapable { get; init; }

    public string? IPv4Address { get; init; }

    public int? IPv4PrefixLength { get; init; }

    public string? DefaultGateway { get; init; }

    public string? DnsSuffix { get; init; }

    public string? DhcpServer { get; init; }

    public string? SystemVersion { get; init; }

    public string? SystemFamily { get; init; }

    public string? SystemSku { get; init; }

    public string? AssetTag { get; init; }

    public string? BaseboardProduct { get; init; }

    public string? BiosVersion { get; init; }

    public string? BiosDate { get; init; }
}
