// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// The members after Error are for trees and left out of the JSON while unset, so Format 1 reads and writes as before.
public sealed record StepRunState(
    Guid StepId,
    StepState State,
    string? Error,
    // How many times the node was entered, so a node inside a repeat starts each new visit with a higher pass. 0 means
    // never.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Pass = 0,
    // Which time through its body a repeat is on, counting from 1.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Iteration = 0,
    // The path an IF took.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IfBranch? Branch = null,
    // The node's tests as they were decided, at most TestEvaluation.MaxPerNode of them.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<TestEvaluation>? Evaluation = null);
