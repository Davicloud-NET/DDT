// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Server.Machines;

public sealed class MachineLogLine
{
    public long Id { get; set; }

    public Guid MachineId { get; set; }

    // The agent's time corrected by MachineLogClock, in UTC.
    public DateTimeOffset TimestampUtc { get; set; }

    // The agent's time as it sent it, in UTC.
    public DateTimeOffset AgentTimestampUtc { get; set; }

    public DateTimeOffset ReceivedUtc { get; set; }

    public AgentLogLevel Level { get; set; }

    public required string Message { get; set; }

    // The machine's active run when the line arrived, without a foreign key, so the log outlives the run's rows.
    public Guid? DeploymentId { get; set; }

    public Guid? StepId { get; set; }
}
