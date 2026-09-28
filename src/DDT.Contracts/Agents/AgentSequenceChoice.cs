// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;
using DDT.Contracts.Images;

namespace DDT.Contracts.Agents;

// A sequence the technician at the machine can pick. RequiredBytes is the disk space the run needs. Suggested marks
// the sequence an assignment rule chose for this machine; a rule only suggests, it never starts a run. RawImageName and
// RawImageBootCapability describe the raw disk image the sequence writes, if any. Inputs are what the machine asks
// after the pick, the answers going with the AgentRunRequest. ComputerName is the name the run gets from the machine's
// own name, the rules or the defaults when the technician types none, such as a rule's PC-{{SerialNumber}}, for a
// sequence that needs one; the computer name question starts with it.
public sealed record AgentSequenceChoice(
    Guid Id,
    string Name,
    string? Description,
    bool ErasesDisk,
    bool NeedsComputerName,
    long RequiredBytes,
    bool Suggested,
    string? RawImageName = null,
    ImageBootCapability? RawImageBootCapability = null,
    UefiCa? RawImageSignedUnder = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<AgentInput>? Inputs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ComputerName = null);
