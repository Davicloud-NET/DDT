// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Agents;

// A running run does not poll next, so every report hands out the tokens a poll would. RunToken is set while the run
// is Running: the agent keeps it on disk and presents it at registration to resume the run after a restart.
//
// While a run waits at its start, Values are its values once the web answered what it waited for, and InputsPending the
// inputs the machine may still answer. ContinueStepId and ContinuePass name the pause someone continued on the web.
// ReportAfterSeconds asks for the next report sooner than usual, while the run waits for someone.
public sealed record AgentRunReportResult(
    string Token,
    string ResumeToken,
    string? RunToken,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Values = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<AgentInput>? InputsPending = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ContinueStepId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ContinuePass = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ReportAfterSeconds = null);
