// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;

namespace DDT.Contracts.Agents;

// ResumeToken is the one the agent was last given, if any. It proves the registration comes from the
// agent that already holds this machine, so its approval survives an expired token or an outage. Disks lists
// only the disks the agent could install on. RunToken is the one the agent kept on disk for its run, to resume the
// run after a restart. SequenceVersion is the highest SequenceDefinition.Version the agent runs, 0 for an agent from
// before task sequences: an agent throws on a step kind it does not know, so the server hands it no newer run.
// SecureBootEnabled is what the firmware says, null when the agent cannot tell or is older than raw disk images.
// TrustedUefiCas says which of Microsoft's third-party UEFI CAs, which sign the shims of Linux distributions, the
// firmware's db holds; null when the agent cannot read or parse db, or is older than this field. ChassisType is the
// SMBIOS System Enclosure's chassis type without its lock bit, so the web can tell a laptop from a desktop; null when
// the firmware lists no enclosure or the agent is older than this field.
public sealed record AgentRegistration(
    string SmbiosUuid,
    string PrimaryMac,
    IReadOnlyList<string> MacAddresses,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string AgentVersion,
    string? ResumeToken = null,
    IReadOnlyList<AgentDisk>? Disks = null,
    string? RunToken = null,
    int SequenceVersion = 0,
    AgentEnvironment Environment = AgentEnvironment.WindowsPE,
    bool? SecureBootEnabled = null,
    UefiCa? TrustedUefiCas = null,
    int? ChassisType = null);
