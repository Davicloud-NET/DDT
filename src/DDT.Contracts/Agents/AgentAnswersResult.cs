// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// The server's response to AgentInputAnswers.
public sealed record AgentAnswersResult(
    // The run's values once nothing is pending, null until then.
    IReadOnlyDictionary<string, string>? Values,
    IReadOnlyList<AgentInput> InputsPending,
    // What was wrong with the answers. The console shows each problem next to its field.
    IReadOnlyList<InputProblem> Problems);
