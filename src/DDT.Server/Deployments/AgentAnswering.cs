// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Server.Deployments;

// What answers given at the machine came to: the agent's answer, or why they were refused. NoSuchRun: the machine has no
// such run.
internal sealed record AgentAnswering(AgentAnswersResult? Result, string? Refusal, bool NoSuchRun = false);
