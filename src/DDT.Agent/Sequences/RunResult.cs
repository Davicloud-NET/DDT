// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Sequences;

// UnsentFailure is the Failed report the server did not get, for the loop to send once it can: the run's own failure,
// or where the run was when the server stopped taking the machine's token.
public sealed record RunResult(RunOutcome Outcome, AgentRunReport? UnsentFailure = null);
