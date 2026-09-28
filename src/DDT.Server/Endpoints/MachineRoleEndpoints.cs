// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Contracts.Messages;
using DDT.Contracts.Rules;
using DDT.Contracts.Values;
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

namespace DDT.Server.Endpoints;

// Machine roles are values that rules give machines together, so like rules only an administrator writes them. A role
// has no problems of its own to be saved with: values a run could not use are refused. A role that rules give cannot be
// deleted until they no longer give it, as a sequence that rules choose cannot, so a rule never gives a role that is
// gone; one that does after a race has a problem that says so, and gives nothing.
public static class MachineRoleEndpoints
{
    public static RouteGroupBuilder MapMachineRoleEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapPost("/", CreateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<MachineRoleView>>> ListAsync(DdtDbContext database, CancellationToken cancellationToken) =>
        TypedResults.Ok<IReadOnlyList<MachineRoleView>>(await RuleViews.ListRolesAsync(database, cancellationToken).ConfigureAwait(false));

    private static async Task<Results<Created<MachineRoleView>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        SaveMachineRoleRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (await RefusalAsync(database, null, request, cancellationToken).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        if (await database.MachineRoles.CountAsync(cancellationToken).ConfigureAwait(false) >= RuleLimits.MaxRoles)
        {
            return ServerProblems.Problem(ServerMessages.MachineRoleTooMany.With("max", RuleLimits.MaxRoles), StatusCodes.Status409Conflict);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        MachineRole role = new() { Id = Guid.CreateVersion7(now), Name = "", NormalizedName = "", Revision = 1, CreatedUtc = now };
        Apply(role, request, user, now);
        database.MachineRoles.Add(role);
        database.AuditEvents.Add(Audit(AuditActions.RoleCreated, role, user, context, now, $"{role.Name}."));

        if (await CommitAsync(database, role, cancellationToken).ConfigureAwait(false) is { } taken)
        {
            return taken;
        }

        MachineRoleView[] roles = await PushAsync(database, live, cancellationToken).ConfigureAwait(false);

        return TypedResults.Created($"/api/machine-roles/{role.Id:D}", roles.Single(r => r.Id == role.Id));
    }

    private static async Task<Results<Ok<MachineRoleView>, NotFound, Conflict<MachineRoleView>, ValidationProblem>> UpdateAsync(
        Guid id,
        SaveMachineRoleRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        MachineRole? role = await database.MachineRoles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);

        if (role is null)
        {
            return TypedResults.NotFound();
        }

        // The client decides what to do with a newer save: take it, or save its own edits over it knowingly.
        if (request.Revision != role.Revision)
        {
            return TypedResults.Conflict(await ViewAsync(database, id, cancellationToken).ConfigureAwait(false));
        }

        if (await RefusalAsync(database, id, request, cancellationToken).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        (string Name, string? Description, string Values) before = (role.Name, role.Description, role.Values);
        DateTimeOffset now = timeProvider.GetUtcNow();
        Apply(role, request, user, now);
        string[] changed =
        [
            .. new (string Field, bool Changed)[]
            {
                ("the name", before.Name != role.Name),
                ("the description", before.Description != role.Description),
                ("the values", before.Values != role.Values),
            }.Where(field => field.Changed).Select(field => field.Field),
        ];

        // An autosave of what is stored already changes nothing and records nothing.
        if (changed.Length == 0)
        {
            database.ChangeTracker.Clear();

            return TypedResults.Ok(await ViewAsync(database, id, cancellationToken).ConfigureAwait(false));
        }

        role.Revision++;
        database.AuditEvents.Add(Audit(AuditActions.RoleChanged, role, user, context, now, $"{role.Name}. Changed {string.Join(", ", changed)}."));

        try
        {
            if (await CommitAsync(database, role, cancellationToken).ConfigureAwait(false) is { } taken)
            {
                return taken;
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();

            return await database.MachineRoles.AnyAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false)
                ? TypedResults.Conflict(await ViewAsync(database, id, cancellationToken).ConfigureAwait(false))
                : TypedResults.NotFound();
        }

        MachineRoleView[] roles = await PushAsync(database, live, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(roles.Single(r => r.Id == id));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(
        Guid id,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        MachineRole? role = await database.MachineRoles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);

        if (role is null)
        {
            return TypedResults.NotFound();
        }

        // RoleIds is JSON, so no foreign key holds the role; the rules are read instead.
        int rules = RuleViews.RuleCount(await RuleBook.LoadAsync(database, cancellationToken).ConfigureAwait(false), id);

        if (rules > 0)
        {
            return ServerProblems.Problem(ServerMessages.MachineRoleGivenByRules.With("count", rules), StatusCodes.Status409Conflict);
        }

        database.MachineRoles.Remove(role);
        database.AuditEvents.Add(Audit(AuditActions.RoleDeleted, role, user, context, timeProvider.GetUtcNow(), $"{role.Name}."));

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.NotFound();
        }

        await PushAsync(database, live, cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    // What keeps a role from being saved: its name, taken or not, and values a run could not use.
    private static async Task<ValidationProblem?> RefusalAsync(
        DdtDbContext database,
        Guid? id,
        SaveMachineRoleRequest request,
        CancellationToken cancellationToken)
    {
        FieldProblems problems = new();
        string name = Text(request.Name) ?? "";

        if (name.Length is 0 or > RuleLimits.MaxRoleNameLength || name.Any(char.IsControl))
        {
            problems.Add("name", ServerMessages.NameLength.With("max", RuleLimits.MaxRoleNameLength));
        }
        else if (await NameTakenAsync(database, id, name, cancellationToken).ConfigureAwait(false))
        {
            problems.Add("name", ServerMessages.MachineRoleNameTaken.With("name", name));
        }

        if (Text(request.Description)?.Length > RuleLimits.MaxDescriptionLength)
        {
            problems.Add("description", ServerMessages.DescriptionLength.With("max", RuleLimits.MaxDescriptionLength));
        }

        if (RuleChecks.ValuesBound(request.Values) is { } bound)
        {
            problems.Add(RuleChecks.ValuesField, bound);
        }
        else
        {
            foreach ((string field, ServerMessage message) in RuleChecks.ValueProblems(RuleDocuments.Clean(request.Values)))
            {
                problems.Add(field, message);
            }
        }

        return problems.Count > 0 ? problems.ToResult() : null;
    }

    private static Task<bool> NameTakenAsync(DdtDbContext database, Guid? id, string name, CancellationToken cancellationToken)
    {
        string normalized = Normalize(name);

        return database.MachineRoles.AnyAsync(r => r.NormalizedName == normalized && r.Id != id, cancellationToken);
    }

    // The unique index settles two saves that took the same name at once; the loser is told as if it had been first.
    private static async Task<ValidationProblem?> CommitAsync(DdtDbContext database, MachineRole role, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return null;
        }
        catch (DbUpdateException exception) when (exception is not DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();

            if (!await NameTakenAsync(database, role.Id, role.Name, cancellationToken).ConfigureAwait(false))
            {
                throw;
            }

            return ServerProblems.Validation("name", ServerMessages.MachineRoleNameTaken.With("name", role.Name));
        }
    }

    private static void Apply(MachineRole role, SaveMachineRoleRequest request, ClaimsPrincipal user, DateTimeOffset now)
    {
        IReadOnlyList<NamedValue> values = RuleDocuments.Clean(request.Values);

        role.Name = Text(request.Name)!;
        role.NormalizedName = Normalize(role.Name);
        role.Description = Text(request.Description);
        role.Values = RuleDocuments.WriteValues(values);
        role.UpdatedUtc = now;
        role.UpdatedByUserId = Principals.UserId(user);
        role.UpdatedByName = user.Identity?.Name;
    }

    private static async Task<MachineRoleView> ViewAsync(DdtDbContext database, Guid id, CancellationToken cancellationToken) =>
        (await RuleViews.ListRolesAsync(database, cancellationToken).ConfigureAwait(false)).Single(r => r.Id == id);

    // A role's values are what a rule's condition may test besides the facts, so the rules go out again too.
    private static async Task<MachineRoleView[]> PushAsync(DdtDbContext database, LiveNotifier live, CancellationToken cancellationToken)
    {
        MachineRoleView[] roles = await RuleViews.ListRolesAsync(database, cancellationToken).ConfigureAwait(false);
        live.RolesChanged(roles);

        if (await database.Rules.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            live.RulesChanged(await RuleViews.ListAsync(database, cancellationToken).ConfigureAwait(false));
        }

        return roles;
    }

    private static string Normalize(string name) => name.Trim().ToUpperInvariant();

    // PostgreSQL text cannot hold a NUL. Null for nothing.
    private static string? Text(string? text)
    {
        string? trimmed = text?.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static AuditEvent Audit(string action, MachineRole role, ClaimsPrincipal user, HttpContext context, DateTimeOffset now, string detail) => new()
    {
        OccurredUtc = now,
        Action = action,
        ActorUserId = Principals.UserId(user),
        ActorName = user.Identity?.Name,
        SubjectId = role.Id.ToString("D"),
        SourceAddress = context.Connection.RemoteIpAddress?.ToString(),
        Detail = StoredText.Bound(detail, AuditEvent.MaxDetailLength),
    };
}
