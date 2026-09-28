// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// The members after Error are for trees and left out of the JSON while unset, so Format 1 reads and writes as before.
// Pass counts the times the node was entered, so a node inside a repeat starts a new visit with a higher pass; 0 is
// never. Iteration is a repeat's current time through its body, from 1. Branch is the path an IF took. Evaluation holds
// the node's tests as they were decided, at most TestEvaluation.MaxPerNode of them.
public sealed record StepRunState(
    Guid StepId,
    StepState State,
    string? Error,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Pass = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Iteration = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IfBranch? Branch = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<TestEvaluation>? Evaluation = null);
