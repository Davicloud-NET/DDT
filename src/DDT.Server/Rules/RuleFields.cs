// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Rules;
using DDT.Server.Data;
using DDT.Server.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Rules;

// What a rule's save request writes, and what keeps it from being stored at all. Everything else about a rule is a
// problem it is saved with.
internal static class RuleFields
{
    public static async Task<FieldProblems?> RefusalAsync(DdtDbContext database, SaveRuleRequest request, CancellationToken cancellationToken)
    {
        FieldProblems problems = new();
        string name = Text(request.Name) ?? "";

        if (name.Length is 0 or > RuleLimits.MaxNameLength || name.Any(char.IsControl))
        {
            problems.Add("name", ServerMessages.NameLength.With("max", RuleLimits.MaxNameLength));
        }

        if (Text(request.Description)?.Length > RuleLimits.MaxDescriptionLength)
        {
            problems.Add("description", ServerMessages.DescriptionLength.With("max", RuleLimits.MaxDescriptionLength));
        }

        if (RuleChecks.ConditionBound(request.When) is { } large)
        {
            problems.Add(RuleChecks.WhenField, large);
        }

        if (RuleChecks.ValuesBound(request.Values) is { } values)
        {
            problems.Add(RuleChecks.ValuesField, values);
        }

        if ((request.RoleIds ?? []).Distinct().Count() > RuleLimits.MaxRolesPerRule)
        {
            problems.Add(RuleChecks.RoleIdsField, ServerMessages.RuleTooManyRoles.With("max", RuleLimits.MaxRolesPerRule));
        }

        if (request.SequenceId is { } sequenceId
            && !await database.TaskSequences.AnyAsync(s => s.Id == sequenceId, cancellationToken).ConfigureAwait(false))
        {
            problems.Add("sequenceId", ServerMessages.RuleSequenceGone.With());
        }

        return problems.Count > 0 ? problems : null;
    }

    public static void Apply(Rule rule, SaveRuleRequest request, Actor actor, DateTimeOffset now)
    {
        rule.Name = Text(request.Name) ?? "";
        rule.Description = Text(request.Description);
        rule.Enabled = request.Enabled;
        rule.When = RuleDocuments.WriteWhen(request.When);
        rule.TaskSequenceId = request.SequenceId;
        rule.Values = RuleDocuments.WriteValues(RuleDocuments.Clean(request.Values));
        rule.RoleIds = RuleDocuments.WriteRoleIds([.. (request.RoleIds ?? []).Distinct()]);
        rule.UpdatedUtc = now;
        rule.UpdatedByUserId = actor.UserId;
        rule.UpdatedByName = actor.Name;
    }

    public static Rule Copy(Rule rule) => new()
    {
        Name = rule.Name,
        Description = rule.Description,
        Enabled = rule.Enabled,
        When = rule.When,
        TaskSequenceId = rule.TaskSequenceId,
        Values = rule.Values,
        RoleIds = rule.RoleIds,
    };

    // The fields a save changed, for the audit, without what they hold.
    public static string[] Changes(Rule before, Rule after) =>
    [
        .. new (string Field, bool Changed)[]
        {
            ("the name", before.Name != after.Name),
            ("the description", before.Description != after.Description),
            (after.Enabled ? "turned it on" : "turned it off", before.Enabled != after.Enabled),
            ("the condition", before.When != after.When),
            ("the sequence", before.TaskSequenceId != after.TaskSequenceId),
            ("the values", before.Values != after.Values),
            ("the machine roles", before.RoleIds != after.RoleIds),
        }.Where(field => field.Changed).Select(field => field.Field),
    ];

    // PostgreSQL text cannot hold a NUL. Null for nothing.
    private static string? Text(string? text)
    {
        string? trimmed = text?.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
