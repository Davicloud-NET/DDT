// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Agents;

// A running run doesn't poll Next, so every report returns the tokens a poll would.
public sealed record AgentRunReportResult(
    string Token,
    string ResumeToken,
    // Set while the run is Running; the agent keeps it on disk and presents it at registration after a restart.
    string? RunToken,
    // The run's values, set once the web has answered the inputs the run was waiting for before it started.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Values = null,
    // While the run waits to start, the inputs the machine may still answer.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<AgentInput>? InputsPending = null,
    // The pause someone continued on the web.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ContinueStepId = null,
    // Which pass of that pause was continued, for a node inside a repeat.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ContinuePass = null,
    // Asks for the next report sooner than usual, while the run waits for someone.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ReportAfterSeconds = null);
