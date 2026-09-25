// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Server.Machines;

// Disks and EligibleDiskCount are null when the agent is too old to report its disks, and SecureBootEnabled when it
// cannot tell or is older than raw disk images.
public sealed record NormalisedRegistration(
    string SmbiosUuid,
    string PrimaryMac,
    IReadOnlyList<string> MacAddresses,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string AgentVersion,
    string? ResumeToken,
    string? Disks,
    int? EligibleDiskCount,
    string? RunToken = null,
    int SequenceVersion = 0,
    AgentEnvironment Environment = AgentEnvironment.WindowsPE,
    bool? SecureBootEnabled = null);
