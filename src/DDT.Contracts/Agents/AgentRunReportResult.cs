// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// A running run does not poll next, so every report hands out the tokens a poll would. RunToken is set while the run
// is Running: the agent keeps it on disk and presents it at registration to resume the run after a restart.
public sealed record AgentRunReportResult(string Token, string ResumeToken, string? RunToken);
