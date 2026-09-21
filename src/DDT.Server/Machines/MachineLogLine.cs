// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Server.Machines;

public sealed class MachineLogLine
{
    public long Id { get; set; }

    public Guid MachineId { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }

    public DateTimeOffset ReceivedUtc { get; set; }

    public AgentLogLevel Level { get; set; }

    public required string Message { get; set; }
}
