// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;

namespace DDT.Contracts.Agents;

// A run as the agent receives it: the sequence frozen when it was assigned and the files its steps use, never
// secrets. State is Assigned or Running. DiskNumber is the disk chosen at the machine; a run assigned on the web has
// none. ComputerName is the name assigned to the machine, which conditions can test. AllowSecureBootMismatch lets a
// raw disk image that is not signed for Secure Boot be written although the machine has Secure Boot on.
//
// Values are the run's values by name, worked out when it started: the sequence's variables and what rules and
// machine roles set, never an Account input's answer. PendingInputs are the inputs the person at the machine may still
// answer; the run starts once every required input has an answer, from the machine or from the web.
public sealed record AgentRun(
    Guid Id,
    DeploymentState State,
    string SequenceName,
    SequenceDefinition Sequence,
    IReadOnlyList<AgentRunImage> Images,
    IReadOnlyList<AgentRunPackage> Packages,
    int? DiskNumber,
    string? ComputerName,
    bool AllowSecureBootMismatch = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Values = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<AgentInput>? PendingInputs = null);
