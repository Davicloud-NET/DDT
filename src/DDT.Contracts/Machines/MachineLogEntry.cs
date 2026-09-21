// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Contracts.Machines;

// TimestampUtc is the agent's clock, which in Windows PE may be wrong. ReceivedUtc is the server's.
public sealed record MachineLogEntry(
    long Id,
    DateTimeOffset TimestampUtc,
    DateTimeOffset ReceivedUtc,
    AgentLogLevel Level,
    string Message);
