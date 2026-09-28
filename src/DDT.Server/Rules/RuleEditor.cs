// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Data.Common;
using DDT.Contracts.Messages;
using DDT.Contracts.Rules;
using DDT.Server.Data;
using DDT.Server.Endpoints;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DDT.Server.Rules;

// Saves the rules and their order, each change with its audit row, and pushes the whole list, since places move.
// Positions count from 0 at the top and have no gaps.
internal sealed class RuleEditor(DdtDbContext database, LiveNotifier live, TimeProvider timeProvider)
{
    private const int MaxAttempts = 3;

    // A new rule goes to the bottom of the list, below every rule it could take a value or the sequence from.
    public async Task<EditOutcome<RuleView>> CreateAsync(SaveRuleRequest request, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        if (await RuleFields.RefusalAsync(database, request, cancellationToken).ConfigureAwait(false) is { } refused)
        {
            return EditOutcome<RuleView>.Invalid(refused);
        }

        if (await database.Rules.CountAsync(cancellationToken).ConfigureAwait(false) >= RuleLimits.MaxRules)
        {
            return EditOutcome<RuleView>.Refused(ServerMessages.RuleTooMany.With("max", RuleLimits.MaxRules));
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Rule rule = new() { Id = Guid.CreateVersion7(now), Name = "", Revision = 1, CreatedUtc = now };
        RuleFields.Apply(rule, request, actor, now);

        // Two administrators adding a rule at once take the same place, and the unique index keeps the first; the second
        // takes the next one.
        for (int attempt = 1; ; attempt++)
        {
            rule.Position = (await database.Rules.MaxAsync(r => (int?)r.Position, cancellationToken).ConfigureAwait(false) ?? -1) + 1;
            database.Rules.Add(rule);
            database.AuditEvents.Add(AuditEvents.Create(AuditActions.RuleCreated, rule.Id.ToString("D"), actor, now, $"Rule {rule.Position + 1}, {rule.Name}."));

            try
            {
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                break;
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                database.ChangeTracker.Clear();

                // Or the sequence was deleted a moment ago.
                if (await RuleFields.RefusalAsync(database, request, cancellationToken).ConfigureAwait(false) is { } gone)
                {
                    return EditOutcome<RuleView>.Invalid(gone);
                }
            }
        }

        RuleView[] rules = await PushAsync(RuleDocuments.ReadRoleIds(rule.RoleIds).Count > 0, cancellationToken).ConfigureAwait(false);

        return EditOutcome<RuleView>.Done(rules.Single(r => r.Id == rule.Id));
    }

    // A save names the revision it was made on, and one over a newer revision gets the rule as it is now.
    public async Task<EditOutcome<RuleView>> UpdateAsync(Guid id, SaveRuleRequest request, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        Rule? rule = await database.Rules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);

        if (rule is null)
        {
            return EditOutcome<RuleView>.NotFound;
        }

        if (request.Revision != rule.Revision)
        {
            return EditOutcome<RuleView>.Newer(await ViewAsync(id, cancellationToken).ConfigureAwait(false));
        }

        if (await RuleFields.RefusalAsync(database, request, cancellationToken).ConfigureAwait(false) is { } refused)
        {
            return EditOutcome<RuleView>.Invalid(refused);
        }

        Rule before = RuleFields.Copy(rule);
        DateTimeOffset now = timeProvider.GetUtcNow();
        RuleFields.Apply(rule, request, actor, now);
        string[] changed = RuleFields.Changes(before, rule);

        // An autosave of what is stored already changes nothing and records nothing.
        if (changed.Length == 0)
        {
            database.ChangeTracker.Clear();

            return EditOutcome<RuleView>.Done(await ViewAsync(id, cancellationToken).ConfigureAwait(false));
        }

        rule.Revision++;
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.RuleChanged,
            rule.Id.ToString("D"),
            actor,
            now,
            $"Rule {rule.Position + 1}, {rule.Name}. Changed {string.Join(", ", changed)}."));

        if (await CommitAsync(id, request, cancellationToken).ConfigureAwait(false) is { } refusal)
        {
            return refusal;
        }

        RuleView[] rules = await PushAsync(before.RoleIds != rule.RoleIds, cancellationToken).ConfigureAwait(false);

        return EditOutcome<RuleView>.Done(rules.Single(r => r.Id == id));
    }

    // The rules below move up a place, so the answer is the list. Null for a rule that is gone.
    public async Task<RuleView[]?> DeleteAsync(Guid id, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        IExecutionStrategy strategy = database.Database.CreateExecutionStrategy();
        Rule? deleted = await strategy.ExecuteAsync(cancellation => RemoveAsync(id, actor, cancellation), cancellationToken).ConfigureAwait(false);

        if (deleted is null)
        {
            return null;
        }

        database.ChangeTracker.Clear();

        return await PushAsync(RuleDocuments.ReadRoleIds(deleted.RoleIds).Count > 0, cancellationToken).ConfigureAwait(false);
    }

    // The order names every rule once, top first, and the rules are numbered from 0 again in it. An order made before
    // someone added, removed or moved a rule gets the list as it is now.
    public async Task<EditOutcome<IReadOnlyList<RuleView>>> ReorderAsync(IReadOnlyList<Guid> order, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(actor);

        if (order.Distinct().Count() != order.Count)
        {
            FieldProblems repeats = new();
            repeats.Add("ruleIds", ServerMessages.RuleOrderRepeats.With());

            return EditOutcome<IReadOnlyList<RuleView>>.Invalid(repeats);
        }

        IExecutionStrategy strategy = database.Database.CreateExecutionStrategy();
        bool? moved;

        try
        {
            moved = await strategy.ExecuteAsync(cancellation => MoveAllAsync(order, actor, cancellation), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is DbUpdateException or DbException)
        {
            // A rule was deleted or moved meanwhile, and a place was taken twice.
            moved = null;
        }

        database.ChangeTracker.Clear();

        if (moved is null)
        {
            return EditOutcome<IReadOnlyList<RuleView>>.Newer(await RuleViews.ListAsync(database, cancellationToken).ConfigureAwait(false));
        }

        RuleView[] list = moved == true
            ? await PushAsync(roles: false, cancellationToken).ConfigureAwait(false)
            : await RuleViews.ListAsync(database, cancellationToken).ConfigureAwait(false);

        return EditOutcome<IReadOnlyList<RuleView>>.Done(list);
    }

    // A refusal when the save lost to a change of the rule, or to the deletion of its sequence.
    private async Task<EditOutcome<RuleView>?> CommitAsync(Guid id, SaveRuleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();

            return await database.Rules.AnyAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false)
                ? EditOutcome<RuleView>.Newer(await ViewAsync(id, cancellationToken).ConfigureAwait(false))
                : EditOutcome<RuleView>.NotFound;
        }
        catch (DbUpdateException)
        {
            // The sequence was deleted a moment ago, and its foreign key refused the save.
            database.ChangeTracker.Clear();

            if (await RuleFields.RefusalAsync(database, request, cancellationToken).ConfigureAwait(false) is { } gone)
            {
                return EditOutcome<RuleView>.Invalid(gone);
            }

            throw;
        }
    }

    // In one transaction with the moves of the rules below, so the list never has a gap.
    private async Task<Rule?> RemoveAsync(Guid id, Actor actor, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        Rule? rule = await database.Rules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);

        if (rule is null)
        {
            return null;
        }

        database.Rules.Remove(rule);
        database.AuditEvents.Add(AuditEvents.Create(AuditActions.RuleDeleted, rule.Id.ToString("D"), actor, timeProvider.GetUtcNow(), $"Rule {rule.Position + 1}, {rule.Name}."));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // One at a time from the top, so each moves into a place that is free: the index is checked row by row.
        List<Guid> below = await database.Rules
            .Where(r => r.Position > rule.Position)
            .OrderBy(r => r.Position)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (Guid moving in below)
        {
            await database.Rules
                .Where(r => r.Id == moving)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.Position, r => r.Position - 1), cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return rule;
    }

    // Null when the order does not name every rule once; false when no rule moves.
    private async Task<bool?> MoveAllAsync(IReadOnlyList<Guid> order, Actor actor, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<Guid, Rule> rules = await database.Rules.AsNoTracking().ToDictionaryAsync(r => r.Id, cancellationToken).ConfigureAwait(false);

        if (rules.Count != order.Count || !order.All(rules.ContainsKey))
        {
            return null;
        }

        List<(Rule Rule, int Position)> moves =
        [
            .. order.Select((ruleId, position) => (Rule: rules[ruleId], Position: position)).Where(move => move.Rule.Position != move.Position),
        ];

        if (moves.Count == 0)
        {
            return false;
        }

        // Out of the way first, into places no rule has, then into the new ones: the index is checked row by row.
        foreach ((Rule rule, int position) in moves)
        {
            await MoveAsync(rule.Id, -1 - position, cancellationToken).ConfigureAwait(false);
        }

        foreach ((Rule rule, int position) in moves)
        {
            await MoveAsync(rule.Id, position, cancellationToken).ConfigureAwait(false);
        }

        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.RuleReordered,
            null,
            actor,
            timeProvider.GetUtcNow(),
            $"Moved {string.Join("; ", moves.Select(move => $"{move.Rule.Name} from {move.Rule.Position + 1} to {move.Position + 1}"))}."));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    private Task<int> MoveAsync(Guid id, int position, CancellationToken cancellationToken) =>
        database.Rules.Where(r => r.Id == id).ExecuteUpdateAsync(set => set.SetProperty(r => r.Position, position), cancellationToken);

    private async Task<RuleView> ViewAsync(Guid id, CancellationToken cancellationToken) =>
        (await RuleViews.ListAsync(database, cancellationToken).ConfigureAwait(false)).Single(r => r.Id == id);

    // Every change goes out as the whole list, and to the machine roles too where it changed how many rules give one.
    private async Task<RuleView[]> PushAsync(bool roles, CancellationToken cancellationToken)
    {
        RuleView[] rules = await RuleViews.ListAsync(database, cancellationToken).ConfigureAwait(false);
        live.RulesChanged(rules);

        if (roles)
        {
            live.RolesChanged(await RuleViews.ListRolesAsync(database, cancellationToken).ConfigureAwait(false));
        }

        return rules;
    }
}
