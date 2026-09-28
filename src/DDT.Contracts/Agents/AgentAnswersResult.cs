// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// What the server made of AgentInputAnswers. Values are the run's values once nothing is pending, and null until then.
// InputsPending are the inputs the machine still has to answer, Problems what was wrong with the answers given, which
// the console shows at their fields.
public sealed record AgentAnswersResult(
    IReadOnlyDictionary<string, string>? Values,
    IReadOnlyList<AgentInput> InputsPending,
    IReadOnlyList<InputProblem> Problems);

// Message is English, in the words the agent logs.
public sealed record InputProblem(string Name, string Message);
