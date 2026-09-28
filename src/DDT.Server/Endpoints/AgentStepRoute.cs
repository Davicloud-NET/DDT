// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Endpoints;

// A step of a machine's run, as the agent's secret routes name it; bound with [AsParameters].
internal sealed record AgentStepRoute(Guid Id, Guid RunId, Guid StepId);
