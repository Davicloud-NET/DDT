// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Server.Deployments;

// Error is why the run couldn't start, which ended it. InputsPending are asked while it waits at its start for a
// required input. If both are null, the run started.
public sealed record RunStart(string? Error, IReadOnlyList<AgentInput>? InputsPending);
