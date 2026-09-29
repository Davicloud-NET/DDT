// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// A running deployment doesn't poll Next, so every report returns the tokens a poll would.
public sealed record AgentDeploymentReportResult(string Token, string ResumeToken);
