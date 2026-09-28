// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;
using DDT.Contracts.Images;
using DDT.Contracts.Machines;

namespace DDT.Contracts.Agents;

// What the agent sends when it registers a machine. In every agent record, members from version 3 sequences on are
// left out of the JSON while unset, so an older server or agent reads the same JSON as before.
public sealed record AgentRegistration(
    string SmbiosUuid,
    string PrimaryMac,
    IReadOnlyList<string> MacAddresses,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string AgentVersion,
    // The token the agent was last given. It proves the registration comes from the agent that already holds this
    // machine, so its approval survives an expired token or an outage.
    string? ResumeToken = null,
    // Only the disks the agent could install on.
    IReadOnlyList<AgentDisk>? Disks = null,
    // The token the agent kept on disk for its run, to resume the run after a restart.
    string? RunToken = null,
    // The highest SequenceDefinition.Version the agent runs, 0 from an agent that predates task sequences. An agent
    // throws on a step kind it does not know, so the server hands it no newer run.
    int SequenceVersion = 0,
    AgentEnvironment Environment = AgentEnvironment.WindowsPE,
    // What the firmware says; null when the agent cannot tell or predates raw disk images.
    bool? SecureBootEnabled = null,
    // Which of Microsoft's third-party UEFI CAs (they sign the Linux shims) the firmware's db holds; null when the
    // agent cannot read or parse db, or predates this member.
    UefiCa? TrustedUefiCas = null,
    // The SMBIOS type 3 chassis type without its lock bit, so the web can tell a laptop from a desktop; null when the
    // firmware lists no enclosure or the agent predates this member.
    int? ChassisType = null,
    // The rest of what conditions test; null from an agent that predates version 3 sequences.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MachineFacts? Facts = null);
