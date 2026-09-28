// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// One list of nodes inside a container. Name is the camelCase JSON member that holds it: "steps" for a group or a
// repeat, "then" and "else" for an IF.
public sealed record StepBody(string Name, IReadOnlyList<SequenceStep> Steps)
{
    public const string StepsName = "steps";
    public const string ThenName = "then";
    public const string ElseName = "else";
}
