// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Endpoints;

// A machine's run, as the agent's report route names it. Bound with [AsParameters].
internal sealed record AgentRunRoute(Guid Id, Guid RunId);
