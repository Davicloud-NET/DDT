// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Rules;
using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Rules;

public static class AssignmentRuleViews
{
    // Every rule, as the Rules page lists them: by kind, then by what they match. A server has a few dozen, so the whole
    // list is also what a change pushes, and pages never have to place one rule among the others themselves.
    public static async Task<AssignmentRuleView[]> ListAsync(DdtDbContext database, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        List<AssignmentRule> rules = await database.AssignmentRules.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<Guid, string> names = await SequenceNamesAsync(database, cancellationToken).ConfigureAwait(false);

        return
        [
            .. rules
                .OrderBy(r => r.Kind)
                .ThenBy(r => r.MatchKey, StringComparer.Ordinal)
                .Select(r => From(r, names[r.TaskSequenceId])),
        ];
    }

    public static AssignmentRuleView From(AssignmentRule rule, string sequenceName)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return new AssignmentRuleView(
            rule.Id,
            rule.Kind,
            rule.Mac,
            rule.Manufacturer,
            rule.Model,
            rule.TaskSequenceId,
            sequenceName,
            rule.Description,
            rule.UpdatedUtc,
            rule.UpdatedByName);
    }

    public static Task<Dictionary<Guid, string>> SequenceNamesAsync(DdtDbContext database, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        return database.TaskSequences.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
    }
}
