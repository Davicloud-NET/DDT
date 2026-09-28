// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// Fetched only while the step runs and kept only in memory; the agent hands none of it to a script.
// RunAs applies in the Windows phase only; the server works out the shares' paths.
public sealed record AgentStepAccounts(AgentAccount? RunAs, IReadOnlyList<AgentShareConnection> Shares);
