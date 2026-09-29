// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Machines;

// What the agent found out about the machine besides its identity. Conditions and rules test it, and the machine's
// page shows it. Every member is null when the agent couldn't tell.
public sealed record MachineFacts
{
    public long? MemoryMegabytes { get; init; }

    public string? ProcessorName { get; init; }

    public int? ProcessorCores { get; init; }

    public int? LogicalProcessors { get; init; }

    public bool? TpmPresent { get; init; }

    // 2.0 or 1.2.
    public string? TpmVersion { get; init; }

    public bool? SecureBootCapable { get; init; }

    // The network members describe the primary adapter, the one PrimaryMac names.
    public string? IPv4Address { get; init; }

    public int? IPv4PrefixLength { get; init; }

    public string? DefaultGateway { get; init; }

    public string? DnsSuffix { get; init; }

    public string? DhcpServer { get; init; }

    // SystemVersion, SystemFamily and SystemSku come from SMBIOS type 1.
    public string? SystemVersion { get; init; }

    public string? SystemFamily { get; init; }

    public string? SystemSku { get; init; }

    // The enclosure's asset tag, from SMBIOS type 3.
    public string? AssetTag { get; init; }

    // From SMBIOS type 2.
    public string? BaseboardProduct { get; init; }

    // BiosVersion and BiosDate come from SMBIOS type 0.
    public string? BiosVersion { get; init; }

    // yyyy-MM-dd.
    public string? BiosDate { get; init; }
}
