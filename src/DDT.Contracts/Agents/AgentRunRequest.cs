// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Agents;

// The technician's pick at the machine.
public sealed record AgentRunRequest(
    Guid SequenceId,
    // Set only for a sequence that erases a disk.
    int? DiskNumber,
    string? ComputerName,
    // The technician confirmed writing an image that is not signed for Secure Boot.
    bool AllowSecureBootMismatch = false,
    // Answers to AgentSequenceChoice.Inputs.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<InputAnswer>? Answers = null);
