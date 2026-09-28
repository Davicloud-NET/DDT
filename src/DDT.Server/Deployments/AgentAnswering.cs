// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Server.Deployments;

// The result of answers given at the machine: the agent's answer, or why they were refused. NoSuchRun means the machine
// has no such run.
internal sealed record AgentAnswering(AgentAnswersResult? Result, string? Refusal, bool NoSuchRun = false);
