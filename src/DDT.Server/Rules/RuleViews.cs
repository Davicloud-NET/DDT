// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Rules;
using DDT.Core.Sequences;
using DDT.Server.Data;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Rules;

// Every rule, top first, and every machine role, as the pages list them. A server has a few dozen of each, so a change
// pushes the whole list: one change can move rules, rename a sequence several choose, or change what others test.
public static class RuleViews
{
    public static async Task<RuleView[]> ListAsync(DdtDbContext database, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        RuleBook book = await RuleBook.LoadAsync(database, cancellationToken).ConfigureAwait(false);
        Dictionary<Guid, string> sequences = await database.TaskSequences
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken)
            .ConfigureAwait(false);
        List<Machine> machines = await database.Machines.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        return List(book, sequences, machines);
    }

    public static RuleView[] List(RuleBook book, IReadOnlyDictionary<Guid, string> sequences, IEnumerable<Machine> machines)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(sequences);
        ArgumentNullException.ThrowIfNull(machines);

        // Every known machine is walked once, as the resolver walks it, so a rule that tests what a rule above it sets is
        // counted as it would match.
        Dictionary<Guid, int> counts = [];

        foreach (Machine machine in machines)
        {
            MachineVariables variables = MachineVariableReader.Read(machine);

            foreach (Guid id in book.Match(variables, MachineValues.Own(machine)).Holding)
            {
                counts[id] = counts.GetValueOrDefault(id) + 1;
            }
        }

        return [.. book.Rules.Select(rule => From(rule, sequences, counts.GetValueOrDefault(rule.Rule.Id)))];
    }

    public static RuleView From(RuleEntry entry, IReadOnlyDictionary<Guid, string> sequences, int matchingMachines)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(sequences);

        Rule rule = entry.Rule;

        return new RuleView(
            rule.Id,
            rule.Position,
            rule.Name,
            rule.Description,
            rule.Enabled,
            entry.When,
            rule.TaskSequenceId,
            rule.TaskSequenceId is { } sequenceId ? sequences.GetValueOrDefault(sequenceId) : null,
            entry.Values,
            entry.RoleIds,
            rule.Revision,
            entry.Problems,
            matchingMachines,
            rule.UpdatedUtc,
            rule.UpdatedByName);
    }

    // By name, ignoring case. RuleCount is how many rules give the role.
    public static async Task<MachineRoleView[]> ListRolesAsync(DdtDbContext database, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        RuleBook book = await RuleBook.LoadAsync(database, cancellationToken).ConfigureAwait(false);

        return
        [
            .. book.Roles.Values
                .OrderBy(role => role.Role.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(role => role.Role.Id)
                .Select(role => Role(role, RuleCount(book, role.Role.Id))),
        ];
    }

    public static MachineRoleView Role(RoleEntry entry, int ruleCount)
    {
        ArgumentNullException.ThrowIfNull(entry);

        MachineRole role = entry.Role;

        return new MachineRoleView(role.Id, role.Name, role.Description, entry.Values, role.Revision, ruleCount, role.UpdatedUtc, role.UpdatedByName);
    }

    public static int RuleCount(RuleBook book, Guid roleId)
    {
        ArgumentNullException.ThrowIfNull(book);

        return book.Rules.Count(rule => rule.RoleIds.Contains(roleId));
    }
}
