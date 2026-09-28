// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Contracts.Values;

namespace DDT.Contracts.Rules;

// One rule of the ordered list. A rule whose When holds for a machine, or that has none, chooses SequenceId, sets
// Values and gives the machine roles RoleIds; the first rule to choose a sequence or set a value wins it. Rules never
// authorize a machine.
public sealed record RuleView(
    Guid Id,
    // Counts from 0 at the top.
    int Position,
    string Name,
    string? Description,
    bool Enabled,
    ConditionNode? When,
    Guid? SequenceId,
    string? SequenceName,
    IReadOnlyList<NamedValue> Values,
    IReadOnlyList<Guid> RoleIds,
    long Revision,
    // Keep the rule from matching until they are fixed. A problem's StepId is null and its Field the path within the
    // rule, such as "when.parts[0].value".
    IReadOnlyList<SequenceProblem> Problems,
    int MatchingMachines,
    DateTimeOffset UpdatedUtc,
    string? UpdatedBy);
