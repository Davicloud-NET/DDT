// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Contracts.Values;

namespace DDT.Contracts.Rules;

// One rule of the ordered list. A rule applies to a machine when its When holds, or when it has no When. It then
// chooses SequenceId, sets Values and gives the machine roles RoleIds. The first rule to choose a sequence or set a
// value wins. Rules never authorize a machine.
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
    // Problems that keep the rule from matching until they're fixed. A problem's StepId is null, and its Field is the
    // path within the rule, such as "when.parts[0].value".
    IReadOnlyList<SequenceProblem> Problems,
    int MatchingMachines,
    DateTimeOffset UpdatedUtc,
    string? UpdatedBy);
