// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Images;
using DDT.Contracts.Machines;

namespace DDT.Server.Machines;

// Disks and EligibleDiskCount are null when the agent is too old to report its disks, and SecureBootEnabled and
// TrustedUefiCas when it cannot tell or is older than raw disk images. ChassisType is null when the firmware lists no
// enclosure, the agent is older than the field, or it sent a value no chassis type can have. Facts is null from an agent
// older than version 3 sequences, and each of its members where the agent could not tell or sent what cannot be right.
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
    bool? SecureBootEnabled = null,
    UefiCa? TrustedUefiCas = null,
    int? ChassisType = null,
    MachineFacts? Facts = null);
