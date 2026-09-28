// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Server.Deployments;

// What answers to a waiting run came to.
public sealed record RunAnswering(
    // The run's answers as they were, which RunAnswerSaves compares so that whoever answered first wins.
    string? Before,
    // What the machine asked before these answers.
    IReadOnlyList<AgentInput> Asked,
    IReadOnlyList<AnswerProblem> Problems,
    // The run's values with these answers; null when refused or when there are problems.
    RunValueCheck? Check,
    // The run waits for no answers.
    bool Refused);
