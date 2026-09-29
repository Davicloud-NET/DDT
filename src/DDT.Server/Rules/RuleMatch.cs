// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Values;

namespace DDT.Server.Rules;

// What the rules say about one machine.
public sealed record RuleMatch(
    // The enabled rules without problems that match it, top first.
    IReadOnlyList<RuleEntry> Matched,
    // The first of them that chooses a sequence.
    RuleEntry? Chooser,
    // The values they set, top rule first, which is the order ValueSources expects.
    IReadOnlyList<ValueSet> RuleValues,
    // What their machine roles set, each role once, in the order the rules gave them.
    IReadOnlyList<ValueSet> RoleValues,
    // The rules whose condition holds, disabled ones too, for counting the machines a rule would match.
    IReadOnlyList<Guid> Holding)
{
    public IReadOnlyList<Guid> MatchedRuleIds => [.. Matched.Select(rule => rule.Rule.Id)];
}
