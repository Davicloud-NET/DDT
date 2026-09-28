// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;

namespace DDT.Contracts.Agents;

// Every poll hands out fresh tokens, so they only expire when the agent cannot reach the server.
public sealed record AgentNextResult(
    MachineState State,
    string Token,
    string ResumeToken,
    int PollAfterSeconds,
    string? SignedInBy,
    AgentDeployment? Deployment = null,
    bool CanPickImage = false,
    bool DomainConfigured = false,
    string? AssignedName = null,
    // Run and the members after it are new rather than a reshaped Deployment, so an agent that predates task sequences
    // never mistakes a run for an image deployment.
    AgentRun? Run = null,
    bool CanPickSequence = false,
    // The sequence an assignment rule chose.
    Guid? SuggestedSequenceId = null);
