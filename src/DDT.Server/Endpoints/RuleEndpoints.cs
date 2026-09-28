// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Data.Common;
using System.Security.Claims;
using DDT.Contracts.Messages;
using DDT.Contracts.Rules;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Rules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DDT.Server.Endpoints;

// Rules are standing configuration that choose code to run as SYSTEM, and the values it runs with, without anyone
// choosing each time, so only an administrator writes them. A save names the revision it was made on, and a save over a
// newer one is refused with the rule as it is now. Every answer is what changed: the rule, or the whole list where
// places moved. Positions count from 0 at the top and have no gaps.
public static class RuleEndpoints
{
    public static RouteGroupBuilder MapRuleEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapPost("/", CreateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/order", ReorderAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<RuleView>>> ListAsync(DdtDbContext database, CancellationToken cancellationToken) =>
        TypedResults.Ok<IReadOnlyList<RuleView>>(await RuleViews.ListAsync(database, cancellationToken).ConfigureAwait(false));

    // A new rule goes to the bottom of the list, below every rule it could take a value or the sequence from.
    private static async Task<Results<Created<RuleView>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        SaveRuleRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (await RefusalAsync(database, request, cancellationToken).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        if (await database.Rules.CountAsync(cancellationToken).ConfigureAwait(false) >= RuleLimits.MaxRules)
        {
            return ServerProblems.Problem(ServerMessages.RuleTooMany.With("max", RuleLimits.MaxRules), StatusCodes.Status409Conflict);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Rule rule = new() { Id = Guid.CreateVersion7(now), Name = "", Revision = 1, CreatedUtc = now };
        Apply(rule, request, user, now);

        // Two administrators adding a rule at once take the same place, and the unique index keeps the first; the second
        // takes the next one.
        for (int attempt = 1; ; attempt++)
        {
            rule.Position = (await database.Rules.MaxAsync(r => (int?)r.Position, cancellationToken).ConfigureAwait(false) ?? -1) + 1;
            database.Rules.Add(rule);
            database.AuditEvents.Add(Audit(AuditActions.RuleCreated, rule.Id, user, context, now, $"Rule {rule.Position + 1}, {rule.Name}."));

            try
            {
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                break;
            }
            catch (DbUpdateException) when (attempt < 3)
            {
                database.ChangeTracker.Clear();

                // Or the sequence was deleted a moment ago.
                if (await RefusalAsync(database, request, cancellationToken).ConfigureAwait(false) is { } gone)
                {
                    return gone;
                }
            }
        }

        RuleView[] rules = await PushAsync(database, live, RuleDocuments.ReadRoleIds(rule.RoleIds).Count > 0, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/rules/{rule.Id:D}", rules.Single(r => r.Id == rule.Id));
    }

    private static async Task<Results<Ok<RuleView>, NotFound, Conflict<RuleView>, ValidationProblem>> UpdateAsync(
        Guid id,
        SaveRuleRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        Rule? rule = await database.Rules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);

        if (rule is null)
        {
            return TypedResults.NotFound();
        }

        // The client decides what to do with a newer save: take it, or save its own edits over it knowingly.
        if (request.Revision != rule.Revision)
        {
            return TypedResults.Conflict(await ViewAsync(database, id, cancellationToken).ConfigureAwait(false));
        }

        if (await RefusalAsync(database, request, cancellationToken).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        Rule before = Copy(rule);
        DateTimeOffset now = timeProvider.GetUtcNow();
        Apply(rule, request, user, now);
        string[] changed = Changes(before, rule);

        // An autosave of what is stored already changes nothing and records nothing.
        if (changed.Length == 0)
        {
            database.ChangeTracker.Clear();

            return TypedResults.Ok(await ViewAsync(database, id, cancellationToken).ConfigureAwait(false));
        }

        rule.Revision++;
        database.AuditEvents.Add(Audit(
            AuditActions.RuleChanged,
            rule.Id,
            user,
            context,
            now,
            $"Rule {rule.Position + 1}, {rule.Name}. Changed {string.Join(", ", changed)}."));

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();

            return await database.Rules.AnyAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false)
                ? TypedResults.Conflict(await ViewAsync(database, id, cancellationToken).ConfigureAwait(false))
                : TypedResults.NotFound();
        }
        catch (DbUpdateException)
        {
            // The sequence was deleted a moment ago, and its foreign key refused the save.
            database.ChangeTracker.Clear();

            if (await RefusalAsync(database, request, cancellationToken).ConfigureAwait(false) is { } gone)
            {
                return gone;
            }

            throw;
        }

        RuleView[] rules = await PushAsync(database, live, before.RoleIds != rule.RoleIds, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(rules.Single(r => r.Id == id));
    }

    // The rules below move up a place, so the answer is the list, whose places changed.
    private static async Task<Results<Ok<IReadOnlyList<RuleView>>, NotFound>> DeleteAsync(
        Guid id,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        IExecutionStrategy strategy = database.Database.CreateExecutionStrategy();
        Rule? deleted = await strategy.ExecuteAsync(
            async cancellation =>
            {
                database.ChangeTracker.Clear();
                await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellation).ConfigureAwait(false);
                Rule? rule = await database.Rules.FirstOrDefaultAsync(r => r.Id == id, cancellation).ConfigureAwait(false);

                if (rule is null)
                {
                    return null;
                }

                database.Rules.Remove(rule);
                database.AuditEvents.Add(Audit(
                    AuditActions.RuleDeleted,
                    rule.Id,
                    user,
                    context,
                    timeProvider.GetUtcNow(),
                    $"Rule {rule.Position + 1}, {rule.Name}."));
                await database.SaveChangesAsync(cancellation).ConfigureAwait(false);

                // One at a time from the top, so each moves into a place that is free: the index is checked row by row.
                List<Guid> below = await database.Rules
                    .Where(r => r.Position > rule.Position)
                    .OrderBy(r => r.Position)
                    .Select(r => r.Id)
                    .ToListAsync(cancellation)
                    .ConfigureAwait(false);

                foreach (Guid moving in below)
                {
                    await database.Rules
                        .Where(r => r.Id == moving)
                        .ExecuteUpdateAsync(set => set.SetProperty(r => r.Position, r => r.Position - 1), cancellation)
                        .ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellation).ConfigureAwait(false);

                return rule;
            },
            cancellationToken).ConfigureAwait(false);

        if (deleted is null)
        {
            return TypedResults.NotFound();
        }

        database.ChangeTracker.Clear();
        RuleView[] rules = await PushAsync(database, live, RuleDocuments.ReadRoleIds(deleted.RoleIds).Count > 0, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<RuleView>>(rules);
    }

    // The order names every rule once, top first, and the rules are numbered from 0 again in it. An order made before
    // someone added, removed or moved a rule is refused with the list as it is now.
    private static async Task<Results<Ok<IReadOnlyList<RuleView>>, Conflict<IReadOnlyList<RuleView>>, ValidationProblem>> ReorderAsync(
        ReorderRulesRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> order = request.RuleIds ?? [];

        if (order.Distinct().Count() != order.Count)
        {
            return ServerProblems.Validation("ruleIds", ServerMessages.RuleOrderRepeats.With());
        }

        IExecutionStrategy strategy = database.Database.CreateExecutionStrategy();
        bool? moved;

        try
        {
            moved = await strategy.ExecuteAsync(
                async cancellation =>
                {
                    database.ChangeTracker.Clear();
                    await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellation).ConfigureAwait(false);
                    Dictionary<Guid, Rule> rules = await database.Rules
                        .AsNoTracking()
                        .ToDictionaryAsync(r => r.Id, cancellation)
                        .ConfigureAwait(false);

                    if (rules.Count != order.Count || !order.All(rules.ContainsKey))
                    {
                        return (bool?)null;
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
                        await MoveAsync(database, rule.Id, -1 - position, cancellation).ConfigureAwait(false);
                    }

                    foreach ((Rule rule, int position) in moves)
                    {
                        await MoveAsync(database, rule.Id, position, cancellation).ConfigureAwait(false);
                    }

                    database.AuditEvents.Add(Audit(
                        AuditActions.RuleReordered,
                        null,
                        user,
                        context,
                        timeProvider.GetUtcNow(),
                        $"Moved {string.Join("; ", moves.Select(move => $"{move.Rule.Name} from {move.Rule.Position + 1} to {move.Position + 1}"))}."));
                    await database.SaveChangesAsync(cancellation).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellation).ConfigureAwait(false);

                    return true;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is DbUpdateException or DbException)
        {
            // A rule was deleted or moved meanwhile, and a place was taken twice.
            moved = null;
        }

        database.ChangeTracker.Clear();

        if (moved is null)
        {
            return TypedResults.Conflict<IReadOnlyList<RuleView>>(await RuleViews.ListAsync(database, cancellationToken).ConfigureAwait(false));
        }

        RuleView[] list = moved == true
            ? await PushAsync(database, live, roles: false, cancellationToken).ConfigureAwait(false)
            : await RuleViews.ListAsync(database, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<RuleView>>(list);
    }

    private static Task<int> MoveAsync(DdtDbContext database, Guid id, int position, CancellationToken cancellationToken) =>
        database.Rules.Where(r => r.Id == id).ExecuteUpdateAsync(set => set.SetProperty(r => r.Position, position), cancellationToken);

    // What keeps a request from being stored at all. Everything else about a rule is a problem it is saved with.
    private static async Task<ValidationProblem?> RefusalAsync(DdtDbContext database, SaveRuleRequest request, CancellationToken cancellationToken)
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

        return problems.Count > 0 ? problems.ToResult() : null;
    }

    private static void Apply(Rule rule, SaveRuleRequest request, ClaimsPrincipal user, DateTimeOffset now)
    {
        rule.Name = Text(request.Name)!;
        rule.Description = Text(request.Description);
        rule.Enabled = request.Enabled;
        rule.When = RuleDocuments.WriteWhen(request.When);
        rule.TaskSequenceId = request.SequenceId;
        rule.Values = RuleDocuments.WriteValues(RuleDocuments.Clean(request.Values));
        rule.RoleIds = RuleDocuments.WriteRoleIds([.. (request.RoleIds ?? []).Distinct()]);
        rule.UpdatedUtc = now;
        rule.UpdatedByUserId = Principals.UserId(user);
        rule.UpdatedByName = user.Identity?.Name;
    }

    private static Rule Copy(Rule rule) => new()
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
    private static string[] Changes(Rule before, Rule after) =>
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

    private static async Task<RuleView> ViewAsync(DdtDbContext database, Guid id, CancellationToken cancellationToken) =>
        (await RuleViews.ListAsync(database, cancellationToken).ConfigureAwait(false)).Single(r => r.Id == id);

    // Every change goes out as the whole list, and to the machine roles too where it changed how many rules give one.
    private static async Task<RuleView[]> PushAsync(DdtDbContext database, LiveNotifier live, bool roles, CancellationToken cancellationToken)
    {
        RuleView[] rules = await RuleViews.ListAsync(database, cancellationToken).ConfigureAwait(false);
        live.RulesChanged(rules);

        if (roles)
        {
            live.RolesChanged(await RuleViews.ListRolesAsync(database, cancellationToken).ConfigureAwait(false));
        }

        return rules;
    }

    // PostgreSQL text cannot hold a NUL. Null for nothing.
    private static string? Text(string? text)
    {
        string? trimmed = text?.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    // A name in the detail is whatever an administrator typed, which StoredText keeps storable.
    private static AuditEvent Audit(string action, Guid? ruleId, ClaimsPrincipal user, HttpContext context, DateTimeOffset now, string detail) => new()
    {
        OccurredUtc = now,
        Action = action,
        ActorUserId = Principals.UserId(user),
        ActorName = user.Identity?.Name,
        SubjectId = ruleId?.ToString("D"),
        SourceAddress = context.Connection.RemoteIpAddress?.ToString(),
        Detail = StoredText.Bound(detail, AuditEvent.MaxDetailLength),
    };
}
