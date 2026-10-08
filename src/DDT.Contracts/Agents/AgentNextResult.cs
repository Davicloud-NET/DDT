// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;
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
    // Run and the members after it are new members, not a reshaped Deployment. That way an agent that predates task
    // sequences never mistakes a run for an image deployment.
    AgentRun? Run = null,
    bool CanPickSequence = false,
    // The sequence an assignment rule chose, or the one picked at the machine before it changed its boot environment.
    Guid? SuggestedSequenceId = null,
    // The boot environment the machine's assigned run starts in, when that is not the one this agent runs in. The agent
    // restarts into it and takes its resume token along.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AgentEnvironment? RestartInto = null,
    // Why the machine's assigned run does not start here although the machine is not restarted for it, in a sentence
    // for the console. Null while there is nothing to say.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WaitReason = null);
