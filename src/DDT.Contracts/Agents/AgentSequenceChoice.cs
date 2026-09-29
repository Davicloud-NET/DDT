// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;
using DDT.Contracts.Images;

namespace DDT.Contracts.Agents;

// A sequence the technician at the machine can pick.
public sealed record AgentSequenceChoice(
    Guid Id,
    string Name,
    string? Description,
    bool ErasesDisk,
    bool NeedsComputerName,
    // The disk space the run needs.
    long RequiredBytes,
    // The sequence an assignment rule chose for this machine. A rule only suggests a sequence and never starts a run.
    bool Suggested,
    // The raw disk image the sequence writes, if any.
    string? RawImageName = null,
    ImageBootCapability? RawImageBootCapability = null,
    UefiCa? RawImageSignedUnder = null,
    // What the machine asks after the pick. The answers are sent with the AgentRunRequest.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<AgentInput>? Inputs = null,
    // For a sequence that needs a name, the name the run gets if the technician doesn't type one. It comes from the
    // machine's own name, the rules (such as PC-{{SerialNumber}}) or the defaults. The computer name question is
    // prefilled with it.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ComputerName = null);
