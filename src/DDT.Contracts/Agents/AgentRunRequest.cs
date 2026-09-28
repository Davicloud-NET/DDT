// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Agents;

// The technician's pick at the machine. DiskNumber is set only for a sequence that erases a disk.
// AllowSecureBootMismatch is set when the technician confirmed writing an image that is not signed for Secure Boot.
// Answers are the technician's answers to the sequence's inputs, AgentSequenceChoice.Inputs.
public sealed record AgentRunRequest(
    Guid SequenceId,
    int? DiskNumber,
    string? ComputerName,
    bool AllowSecureBootMismatch = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<InputAnswer>? Answers = null);
