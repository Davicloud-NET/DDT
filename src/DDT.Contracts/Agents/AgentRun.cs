// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;

namespace DDT.Contracts.Agents;

// A run as the agent receives it: the sequence frozen when it was assigned and the files its steps use, never secrets.
public sealed record AgentRun(
    Guid Id,
    // Assigned or Running.
    DeploymentState State,
    string SequenceName,
    SequenceDefinition Sequence,
    IReadOnlyList<AgentRunImage> Images,
    IReadOnlyList<AgentRunPackage> Packages,
    // The disk chosen at the machine; a run assigned on the web has none.
    int? DiskNumber,
    // The name assigned to the machine, which conditions can test.
    string? ComputerName,
    // Lets a raw disk image that is not signed for Secure Boot be written although the machine has Secure Boot on.
    bool AllowSecureBootMismatch = false,
    // Worked out when the run started, from the sequence's variables, rules and machine roles; never an Account
    // input's answer.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Values = null,
    // Inputs the person at the machine may still answer; the run starts once every required one has an answer, from
    // the machine or from the web.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<AgentInput>? PendingInputs = null);
