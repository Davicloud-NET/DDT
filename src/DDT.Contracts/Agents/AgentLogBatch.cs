// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// SentUtc is the agent's clock when it sent the batch. The server corrects the lines' times by the difference to its
// own clock, because the Windows PE clock can be hours off.
public sealed record AgentLogBatch(IReadOnlyList<AgentLogLine> Lines, DateTimeOffset? SentUtc = null);
