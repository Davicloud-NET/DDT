// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Contracts.Machines;

public sealed record MachineLogEntry(
    long Id,
    // The agent's time, corrected by how far its clock was off when it sent the line (hours, in Windows PE).
    // AgentTimestampUtc is the agent's own, ReceivedUtc the server's.
    DateTimeOffset TimestampUtc,
    DateTimeOffset ReceivedUtc,
    AgentLogLevel Level,
    string Message,
    DateTimeOffset AgentTimestampUtc,
    // The run that was active when the line arrived.
    Guid? DeploymentId = null,
    // The step the agent was running then.
    Guid? StepId = null);
