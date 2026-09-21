// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;

namespace DDT.Contracts.Agents;

// State is Running, Done or Failed. Steps holds every step that has left Pending, so a report that is sent again or
// lost changes nothing, and a step that was over between two reports is still seen. Percent is the current step's.
// The server stamps step times, because the Windows PE clock can be hours off.
public sealed record AgentRunReport(
    DeploymentState State,
    SequencePhase Phase,
    IReadOnlyList<StepRunState> Steps,
    Guid? CurrentStepId,
    int Percent,
    RunActivity Activity,
    string? Error);
