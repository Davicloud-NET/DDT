// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Contracts.Values;

namespace DDT.Contracts.Rules;

// One rule of the ordered list, Position counting from 0 at the top. A rule whose When holds for a machine, or that has
// none, chooses SequenceId, sets Values and gives the machine roles RoleIds; the first rule to choose a sequence or set
// a value wins it. Rules never authorize a machine. Problems keep the rule from matching until they are fixed; a
// problem's StepId is null and its Field the path within the rule, such as "when.parts[0].value". MatchingMachines is
// how many known machines the rule matches.
public sealed record RuleView(
    Guid Id,
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
    IReadOnlyList<SequenceProblem> Problems,
    int MatchingMachines,
    DateTimeOffset UpdatedUtc,
    string? UpdatedBy);
