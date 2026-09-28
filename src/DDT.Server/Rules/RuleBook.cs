// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Core.Sequences;
using DDT.Core.Values;
using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Rules;

// The ordered rules and the machine roles, read once for a request: each rule's JSON parsed and its problems found. A
// rule applies when it is enabled and has no problems, and then matches a machine when its When holds or it has none.
public sealed class RuleBook
{
    private RuleBook(IReadOnlyList<RuleEntry> rules, IReadOnlyDictionary<Guid, RoleEntry> roles)
    {
        Rules = rules;
        Roles = roles;
    }

    // By Position, top first.
    public IReadOnlyList<RuleEntry> Rules { get; }

    public IReadOnlyDictionary<Guid, RoleEntry> Roles { get; }

    public static async Task<RuleBook> LoadAsync(DdtDbContext database, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        List<Rule> rules = await database.Rules.AsNoTracking().OrderBy(r => r.Position).ToListAsync(cancellationToken).ConfigureAwait(false);
        List<MachineRole> roles = await database.MachineRoles.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        return From(rules, roles);
    }

    public static RuleBook From(IEnumerable<Rule> rules, IEnumerable<MachineRole> roles)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(roles);

        Dictionary<Guid, RoleEntry> byId = roles.ToDictionary(role => role.Id, role => new RoleEntry(role, RuleDocuments.ReadValues(role.Values)));
        List<(Rule Rule, bool Readable, ConditionNode? When, IReadOnlyList<NamedValue> Values, IReadOnlyList<Guid> RoleIds)> read =
        [
            .. rules.OrderBy(rule => rule.Position).Select(rule =>
            {
                bool readable = RuleDocuments.TryReadWhen(rule.When, out ConditionNode? when);

                return (rule, readable, when, RuleDocuments.ReadValues(rule.Values), RuleDocuments.ReadRoleIds(rule.RoleIds));
            }),
        ];

        // A condition may test any name a rule or a machine role sets, as the page offers them.
        HashSet<string> names = new(
            read.SelectMany(rule => rule.Values).Concat(byId.Values.SelectMany(role => role.Values)).Select(value => value.Name),
            StringComparer.OrdinalIgnoreCase);
        HashSet<Guid> known = [.. byId.Keys];

        return new RuleBook(
            [
                .. read.Select(rule => new RuleEntry(
                    rule.Rule,
                    rule.When,
                    rule.Values,
                    rule.RoleIds,
                    rule.Readable
                        ? RuleChecks.Problems(rule.When, rule.Values, rule.RoleIds, names, known)
                        : [SequenceProblem.From(null, RuleChecks.WhenField, ServerMessages.RuleConditionUnreadable.With())])),
            ],
            byId);
    }

    // Walks the rules from the top over what the machine is. Machine is the machine's facts, MachineValues its own values,
    // which a condition that tests a value sees before those of the rules above it that matched and their machine roles.
    public RuleMatch Match(MachineVariables machine, IReadOnlyList<NamedValue> machineValues)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(machineValues);

        List<RuleEntry> matched = [];
        List<Guid> holding = [];
        List<ValueSet> ruleValues = [];
        List<ValueSet> roleValues = [];
        HashSet<Guid> given = [];
        RuleEntry? chooser = null;
        MachineVariables? withValues = null;

        foreach (RuleEntry rule in Rules)
        {
            if (rule.Problems.Count > 0)
            {
                continue;
            }

            // The values so far are worked out only for a condition that tests one, and again only after a match changed them.
            if (rule.TestsValues)
            {
                withValues ??= WithValues(machine, machineValues, ruleValues, roleValues);
            }

            if (!ConditionEvaluator.Holds(rule.When, rule.TestsValues ? withValues! : machine))
            {
                continue;
            }

            holding.Add(rule.Rule.Id);

            if (!rule.Rule.Enabled)
            {
                continue;
            }

            matched.Add(rule);
            chooser ??= rule.Rule.TaskSequenceId is null ? null : rule;

            if (AddValues(rule, ruleValues, roleValues, given))
            {
                withValues = null;
            }
        }

        return new RuleMatch([.. matched], chooser, [.. ruleValues], [.. roleValues], [.. holding]);
    }

    private static MachineVariables WithValues(
        MachineVariables machine,
        IReadOnlyList<NamedValue> machineValues,
        List<ValueSet> ruleValues,
        List<ValueSet> roleValues)
    {
        ValueResolution so = ValueResolver.Resolve(new ValueSources
        {
            Machine = machineValues,
            Rules = ruleValues,
            Roles = roleValues,
            Facts = machine,
        });

        return machine with { Variables = so.Effective };
    }

    // Whether the matched rule gave values, its own or a machine role's not given yet, which change the values so far.
    private bool AddValues(RuleEntry rule, List<ValueSet> ruleValues, List<ValueSet> roleValues, HashSet<Guid> given)
    {
        bool added = false;

        if (rule.Values.Count > 0)
        {
            ruleValues.Add(new ValueSet(rule.Rule.Id, rule.Rule.Name, rule.Values));
            added = true;
        }

        foreach (Guid roleId in rule.RoleIds)
        {
            if (Roles.TryGetValue(roleId, out RoleEntry? role) && given.Add(roleId))
            {
                roleValues.Add(new ValueSet(role.Role.Id, role.Role.Name, role.Values));
                added = true;
            }
        }

        return added;
    }
}
