// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Contracts.Machines;

// TimestampUtc is the agent's time corrected by how far its clock was off when it sent the line, which in Windows PE
// can be hours; AgentTimestampUtc is the agent's own. ReceivedUtc is the server's. DeploymentId is the run that was
// active when the line arrived, and StepId the step the agent was running then.
public sealed record MachineLogEntry(
    long Id,
    DateTimeOffset TimestampUtc,
    DateTimeOffset ReceivedUtc,
    AgentLogLevel Level,
    string Message,
    DateTimeOffset AgentTimestampUtc,
    Guid? DeploymentId = null,
    Guid? StepId = null);
