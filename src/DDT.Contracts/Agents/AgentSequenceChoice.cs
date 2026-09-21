// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// A sequence the technician at the machine can pick. RequiredBytes is the disk space the run needs. Suggested marks
// the sequence an assignment rule chose for this machine; a rule only suggests, it never starts a run.
public sealed record AgentSequenceChoice(
    Guid Id,
    string Name,
    string? Description,
    bool ErasesDisk,
    bool NeedsComputerName,
    long RequiredBytes,
    bool Suggested);
