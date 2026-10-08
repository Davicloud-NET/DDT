// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;

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
    DeploymentSummary? Deployment = null,
    bool? SecureBootEnabled = null,
    UefiCa? TrustedUefiCas = null,
    DeviceKind DeviceKind = DeviceKind.Unknown,
    // What the agent last registered with; null from an agent that predates version 3 sequences.
    MachineFacts? Facts = null,
    // Where the machine's agent last registered from.
    AgentEnvironment AgentEnvironment = AgentEnvironment.WindowsPE,
    // The boot environment the machine was asked to restart into, until it registers from there.
    AgentEnvironment? NextEnvironment = null,
    // Whether the machine's agent can restart into the other boot environment and stay signed in.
    bool? ChangesBootEnvironment = null,
    // The boot environment the machine did not start after restarting for its run. The server orders no more, and the run waits.
    AgentEnvironment? RestartsFailed = null);
