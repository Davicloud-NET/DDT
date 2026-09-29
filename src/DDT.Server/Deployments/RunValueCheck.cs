// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Values;

namespace DDT.Server.Deployments;

// A run's values as they stand, and what keeps it from starting.
public sealed record RunValueCheck(
    ValueResolution Resolution,
    // The required inputs with neither an answer nor a default.
    IReadOnlyList<InputDeclaration> Missing,
    IReadOnlyList<ValueProblem> Problems,
    // The unanswered inputs the machine asks, required or not.
    IReadOnlyList<AgentInput> AskedAtMachine);
