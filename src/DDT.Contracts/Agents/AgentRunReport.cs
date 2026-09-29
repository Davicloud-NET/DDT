// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;

namespace DDT.Contracts.Agents;

// The server stamps step times, because the Windows PE clock can be hours off.
public sealed record AgentRunReport(
    // Running, Done or Failed.
    DeploymentState State,
    SequencePhase Phase,
    // Every step that has left Pending. That way a repeated or lost report changes nothing, and a step that ended
    // between two reports is still seen.
    IReadOnlyList<StepRunState> Steps,
    Guid? CurrentStepId,
    // The current step's progress.
    int Percent,
    RunActivity Activity,
    string? Error,
    // The sequence's variables as steps have set them so far.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Variables = null,
    // The Pause step's message with its values filled in, while Activity is Paused.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PauseMessage = null);
