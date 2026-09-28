// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Server.Machines;

namespace DDT.Server.Deployments;

// An agent's report with the run and the rows it applies to. Tree is the run's definition where it has containers, whose
// IFs decide the path; null for a flat run. Now is the server's time, which the rows take.
internal sealed record ReceivedReport(
    Machine Machine,
    Deployment Run,
    AgentRunReport Report,
    List<DeploymentStep> Steps,
    SequenceDefinition? Tree,
    string? Address,
    DateTimeOffset Now);
