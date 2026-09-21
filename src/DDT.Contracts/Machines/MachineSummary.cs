// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;

namespace DDT.Contracts.Machines;

public sealed record MachineSummary(
    Guid Id,
    MachineState State,
    string SmbiosUuid,
    string PrimaryMac,
    IReadOnlyList<string> MacAddresses,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? AssignedName,
    string? AgentVersion,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    string? LastSeenAddress,
    string? SignedInBy,
    string? FirstSeenAddress,
    bool EverApproved,
    string? Disks = null,
    int? EligibleDiskCount = null,
    DeploymentSummary? Deployment = null);
