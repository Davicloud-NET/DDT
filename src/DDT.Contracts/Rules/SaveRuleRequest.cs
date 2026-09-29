// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Contracts.Values;

namespace DDT.Contracts.Rules;

// Creates a rule at the end of the list, or saves one.
public sealed record SaveRuleRequest(
    // The revision the page last read. A save over a newer revision is refused and returns the rule as it is now. A
    // new rule doesn't have one, so the server ignores it.
    long Revision,
    string Name,
    string? Description,
    bool Enabled,
    ConditionNode? When,
    Guid? SequenceId,
    IReadOnlyList<NamedValue> Values,
    IReadOnlyList<Guid> RoleIds);
