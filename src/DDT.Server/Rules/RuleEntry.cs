// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Core.Sequences;

namespace DDT.Server.Rules;

// A rule as the RuleBook read it.
public sealed record RuleEntry(
    Rule Rule,
    ConditionNode? When,
    IReadOnlyList<NamedValue> Values,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<SequenceProblem> Problems)
{
    // The condition tests a value rather than a machine fact; ComputerName counts, since a value may set it.
    public bool TestsValues { get; } = RuleChecks.Names(When).Any(name =>
        MachineVariables.Fact(name) is null or MachineVariableNames.ComputerName);
}
