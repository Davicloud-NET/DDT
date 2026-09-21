// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// ResumeToken is the one the agent was last given, if any. It proves the registration comes from the
// agent that already holds this machine, so its approval survives an expired token or an outage. Disks lists
// only the disks the agent could install on.
public sealed record AgentRegistration(
    string SmbiosUuid,
    string PrimaryMac,
    IReadOnlyList<string> MacAddresses,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string AgentVersion,
    string? ResumeToken = null,
    IReadOnlyList<AgentDisk>? Disks = null);
