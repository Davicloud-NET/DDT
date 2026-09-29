// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Rules;
using DDT.Contracts.Values;
using DDT.Server.Data;
using DDT.Server.Endpoints;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Rules;

// Saves machine roles, with an audit row for each change. A role is never saved with problems. Values a run couldn't
// use are refused instead. A role can't be deleted while rules still give it.
internal sealed class MachineRoleEditor(DdtDbContext database, LiveNotifier live, TimeProvider timeProvider)
{
    public async Task<EditOutcome<MachineRoleView>> CreateAsync(SaveMachineRoleRequest request, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        if (await RefusalAsync(null, request, cancellationToken).ConfigureAwait(false) is { } refused)
        {
            return EditOutcome<MachineRoleView>.Invalid(refused);
        }

        if (await database.MachineRoles.CountAsync(cancellationToken).ConfigureAwait(false) >= RuleLimits.MaxRoles)
        {
            return EditOutcome<MachineRoleView>.Refused(ServerMessages.MachineRoleTooMany.With("max", RuleLimits.MaxRoles));
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        MachineRole role = new() { Id = Guid.CreateVersion7(now), Name = "", NormalizedName = "", Revision = 1, CreatedUtc = now };
        Apply(role, request, actor, now);
        database.MachineRoles.Add(role);
        database.AuditEvents.Add(AuditEvents.Create(AuditActions.RoleCreated, role.Id.ToString("D"), actor, now, $"{role.Name}."));

        if (await CommitAsync(role, cancellationToken).ConfigureAwait(false) is { } taken)
        {
            return EditOutcome<MachineRoleView>.Invalid(taken);
        }

        MachineRoleView[] roles = await PushAsync(cancellationToken).ConfigureAwait(false);

        return EditOutcome<MachineRoleView>.Done(roles.Single(r => r.Id == role.Id));
    }

    // A save names the revision it was based on. If the role has a newer revision, the save gets the current one.
    public async Task<EditOutcome<MachineRoleView>> UpdateAsync(Guid id, SaveMachineRoleRequest request, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        MachineRole? role = await database.MachineRoles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);

        if (role is null)
        {
            return EditOutcome<MachineRoleView>.NotFound;
        }

        if (request.Revision != role.Revision)
        {
            return EditOutcome<MachineRoleView>.Newer(await ViewAsync(id, cancellationToken).ConfigureAwait(false));
        }

        if (await RefusalAsync(id, request, cancellationToken).ConfigureAwait(false) is { } refused)
        {
            return EditOutcome<MachineRoleView>.Invalid(refused);
        }

        (string Name, string? Description, string Values) before = (role.Name, role.Description, role.Values);
        DateTimeOffset now = timeProvider.GetUtcNow();
        Apply(role, request, actor, now);
        string[] changed =
        [
            .. new (string Field, bool Changed)[]
            {
                ("the name", before.Name != role.Name),
                ("the description", before.Description != role.Description),
                ("the values", before.Values != role.Values),
            }.Where(field => field.Changed).Select(field => field.Field),
        ];

        // An autosave of what's already stored changes nothing, so nothing is recorded.
        if (changed.Length == 0)
        {
            database.ChangeTracker.Clear();

            return EditOutcome<MachineRoleView>.Done(await ViewAsync(id, cancellationToken).ConfigureAwait(false));
        }

        role.Revision++;
        database.AuditEvents.Add(AuditEvents.Create(AuditActions.RoleChanged, role.Id.ToString("D"), actor, now, $"{role.Name}. Changed {string.Join(", ", changed)}."));

        return await SaveChangedAsync(role, cancellationToken).ConfigureAwait(false);
    }

    // Found is false if the role doesn't exist. Refusal says why it can't be deleted.
    public async Task<(bool Found, ServerMessage? Refusal)> DeleteAsync(Guid id, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        MachineRole? role = await database.MachineRoles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);

        if (role is null)
        {
            return (false, null);
        }

        // RoleIds is JSON, so no foreign key protects the role. The rules are read instead.
        int rules = RuleViews.RuleCount(await RuleBook.LoadAsync(database, cancellationToken).ConfigureAwait(false), id);

        if (rules > 0)
        {
            return (true, ServerMessages.MachineRoleGivenByRules.With("count", rules));
        }

        database.MachineRoles.Remove(role);
        database.AuditEvents.Add(AuditEvents.Create(AuditActions.RoleDeleted, role.Id.ToString("D"), actor, timeProvider.GetUtcNow(), $"{role.Name}."));

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (false, null);
        }

        await PushAsync(cancellationToken).ConfigureAwait(false);

        return (true, null);
    }

    private async Task<EditOutcome<MachineRoleView>> SaveChangedAsync(MachineRole role, CancellationToken cancellationToken)
    {
        try
        {
            if (await CommitAsync(role, cancellationToken).ConfigureAwait(false) is { } taken)
            {
                return EditOutcome<MachineRoleView>.Invalid(taken);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();

            return await database.MachineRoles.AnyAsync(r => r.Id == role.Id, cancellationToken).ConfigureAwait(false)
                ? EditOutcome<MachineRoleView>.Newer(await ViewAsync(role.Id, cancellationToken).ConfigureAwait(false))
                : EditOutcome<MachineRoleView>.NotFound;
        }

        MachineRoleView[] roles = await PushAsync(cancellationToken).ConfigureAwait(false);

        return EditOutcome<MachineRoleView>.Done(roles.Single(r => r.Id == role.Id));
    }

    // What keeps a role from being saved: a bad or taken name, a description that's too long, or values a run couldn't
    // use.
    private async Task<FieldProblems?> RefusalAsync(Guid? id, SaveMachineRoleRequest request, CancellationToken cancellationToken)
    {
        FieldProblems problems = new();
        string name = Text(request.Name) ?? "";

        if (name.Length is 0 or > RuleLimits.MaxRoleNameLength || name.Any(char.IsControl))
        {
            problems.Add("name", ServerMessages.NameLength.With("max", RuleLimits.MaxRoleNameLength));
        }
        else if (await NameTakenAsync(id, name, cancellationToken).ConfigureAwait(false))
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

        return problems.Count > 0 ? problems : null;
    }

    private Task<bool> NameTakenAsync(Guid? id, string name, CancellationToken cancellationToken)
    {
        string normalized = Normalize(name);

        return database.MachineRoles.AnyAsync(r => r.NormalizedName == normalized && r.Id != id, cancellationToken);
    }

    // The unique index decides between two saves that take the same name at once. The loser is told the name is taken,
    // just as if the other save had come first.
    private async Task<FieldProblems?> CommitAsync(MachineRole role, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return null;
        }
        catch (DbUpdateException exception) when (exception is not DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();

            if (!await NameTakenAsync(role.Id, role.Name, cancellationToken).ConfigureAwait(false))
            {
                throw;
            }

            FieldProblems taken = new();
            taken.Add("name", ServerMessages.MachineRoleNameTaken.With("name", role.Name));

            return taken;
        }
    }

    private static void Apply(MachineRole role, SaveMachineRoleRequest request, Actor actor, DateTimeOffset now)
    {
        IReadOnlyList<NamedValue> values = RuleDocuments.Clean(request.Values);

        role.Name = Text(request.Name) ?? "";
        role.NormalizedName = Normalize(role.Name);
        role.Description = Text(request.Description);
        role.Values = RuleDocuments.WriteValues(values);
        role.UpdatedUtc = now;
        role.UpdatedByUserId = actor.UserId;
        role.UpdatedByName = actor.Name;
    }

    private async Task<MachineRoleView> ViewAsync(Guid id, CancellationToken cancellationToken) =>
        (await RuleViews.ListRolesAsync(database, cancellationToken).ConfigureAwait(false)).Single(r => r.Id == id);

    // A rule's condition may test a role's values as well as the facts, so the rules are pushed again too.
    private async Task<MachineRoleView[]> PushAsync(CancellationToken cancellationToken)
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

    // PostgreSQL text can't hold a NUL. Returns null for empty text.
    private static string? Text(string? text)
    {
        string? trimmed = text?.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
