// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Server.Deployments;

// The result of answers to a waiting run.
public sealed record RunAnswering(
    // The run's answers before these. RunAnswerSaves compares them, so whoever answered first wins.
    string? Before,
    // What the machine asked before these answers.
    IReadOnlyList<AgentInput> Asked,
    IReadOnlyList<AnswerProblem> Problems,
    // The run's values with these answers. Null if refused or if there are problems.
    RunValueCheck? Check,
    // The run doesn't wait for answers.
    bool Refused);
