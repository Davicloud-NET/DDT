// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Contracts.Agents;

namespace DDT.Server.Deployments;

// An agent's report as its request brings it. User is the machine's token, whose generation is checked against the
// machine as it is stored.
internal sealed record IncomingReport(Guid MachineId, Guid RunId, AgentRunReport Report, ClaimsPrincipal User, string? Address);
