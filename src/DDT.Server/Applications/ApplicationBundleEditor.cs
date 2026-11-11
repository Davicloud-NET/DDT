// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Core.Applications;
using DDT.Server.Data;
using DDT.Server.Endpoints;
using DDT.Server.Images;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Applications;

// Saves the bundles of the library, each change audited and pushed, under the library lock like applications, so a
// name stays unique across both.
internal sealed class ApplicationBundleEditor(
    DdtDbContext database,
    ImageStore store,
    ApplicationViews views,
    LiveNotifier live,
    TimeProvider timeProvider)
{
    public async Task<EditOutcome<ApplicationBundleView>> CreateAsync(SaveApplicationBundleRequest request, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        await using SemaphoreHold hold = await store.LibraryLock.EnterAsync(cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = timeProvider.GetUtcNow();
        Guid id = Guid.CreateVersion7(now);
        ApplicationRows rows = await views.RowsAsync(cancellationToken).ConfigureAwait(false);

        if (ApplicationLibraryChecks.Check(request, id, rows) is { Count: > 0 } problems)
        {
            return EditOutcome<ApplicationBundleView>.Invalid(problems);
        }

        ApplicationBundle bundle = new() { Id = id, Name = "", NormalizedName = "", Revision = 1, CreatedUtc = now };
        Apply(bundle, request, actor, now);
        database.ApplicationBundles.Add(bundle);
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.ApplicationBundleCreated,
            id.ToString("D"),
            actor,
            now,
            $"Created the bundle {bundle.Name}: {Members(request.Members, rows)}."));

        return await SavedAsync(bundle, request.Members, cancellationToken).ConfigureAwait(false);
    }

    public async Task<EditOutcome<ApplicationBundleView>> SaveAsync(
        Guid id,
        SaveApplicationBundleRequest request,
        Actor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        await using SemaphoreHold hold = await store.LibraryLock.EnterAsync(cancellationToken).ConfigureAwait(false);

        ApplicationBundle? bundle = await database.ApplicationBundles.FirstOrDefaultAsync(b => b.Id == id, cancellationToken).ConfigureAwait(false);

        if (bundle is null)
        {
            return EditOutcome<ApplicationBundleView>.NotFound;
        }

        if (request.Revision != bundle.Revision)
        {
            return EditOutcome<ApplicationBundleView>.Newer(ApplicationViews.View(bundle));
        }

        ApplicationRows rows = await views.RowsAsync(cancellationToken).ConfigureAwait(false);

        if (ApplicationLibraryChecks.Check(request, id, rows) is { Count: > 0 } problems)
        {
            return EditOutcome<ApplicationBundleView>.Invalid(problems);
        }

        List<string> changes = Changes(bundle, request, rows);

        if (changes.Count == 0)
        {
            return EditOutcome<ApplicationBundleView>.Done(ApplicationViews.View(bundle));
        }

        IReadOnlyList<Guid> before = ApplicationColumns.ReadIds(bundle.Members);
        bool renamed = bundle.Name != ApplicationNames.Clean(request.Name);
        DateTimeOffset now = timeProvider.GetUtcNow();

        Apply(bundle, request, actor, now);
        bundle.Revision++;
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.ApplicationBundleChanged,
            id.ToString("D"),
            actor,
            now,
            $"Changed the bundle {bundle.Name}: {string.Join(", ", changes)}."));

        // A member shows its bundles by name, so a rename changes every one of them.
        IEnumerable<Guid> affected = renamed ? before.Union(request.Members) : before.Except(request.Members).Union(request.Members.Except(before));

        return await SavedAsync(bundle, [.. affected], cancellationToken).ConfigureAwait(false);
    }

    // Found is false if the bundle doesn't exist. Sequences name bundles once a step can; then their use refuses this.
    public async Task<bool> DeleteAsync(Guid id, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        await using SemaphoreHold hold = await store.LibraryLock.EnterAsync(cancellationToken).ConfigureAwait(false);

        ApplicationBundle? bundle = await database.ApplicationBundles.FirstOrDefaultAsync(b => b.Id == id, cancellationToken).ConfigureAwait(false);

        if (bundle is null)
        {
            return false;
        }

        database.ApplicationBundles.Remove(bundle);
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.ApplicationBundleDeleted,
            id.ToString("D"),
            actor,
            timeProvider.GetUtcNow(),
            $"Deleted the bundle {bundle.Name}."));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        live.ApplicationBundlesRemoved([id]);
        await views.PushAsync(live, ApplicationColumns.ReadIds(bundle.Members), cancellationToken).ConfigureAwait(false);

        return true;
    }

    private static void Apply(ApplicationBundle bundle, SaveApplicationBundleRequest request, Actor actor, DateTimeOffset now)
    {
        bundle.Name = ApplicationNames.Clean(request.Name) ?? "";
        bundle.NormalizedName = ApplicationNames.Normalize(request.Name) ?? "";
        bundle.Description = ApplicationFields.Optional(request.Description);
        bundle.Members = ApplicationColumns.WriteIds(request.Members);
        bundle.UpdatedUtc = now;
        bundle.UpdatedByUserId = actor.UserId;
        bundle.UpdatedByName = actor.Name;
    }

    private static List<string> Changes(ApplicationBundle bundle, SaveApplicationBundleRequest request, ApplicationRows rows)
    {
        List<string> changes = [];
        string name = ApplicationNames.Clean(request.Name) ?? "";
        string? description = ApplicationFields.Optional(request.Description);

        if (bundle.Name != name)
        {
            changes.Add($"name '{bundle.Name}' to '{name}'");
        }

        if (bundle.Description != description)
        {
            changes.Add($"description '{bundle.Description}' to '{description}'");
        }

        if (!ApplicationColumns.ReadIds(bundle.Members).SequenceEqual(request.Members))
        {
            changes.Add($"members {Members(request.Members, rows)}");
        }

        return changes;
    }

    private static string Members(IReadOnlyList<Guid> members, ApplicationRows rows) => string.Join("; ", rows.NamesOf(members));

    private async Task<EditOutcome<ApplicationBundleView>> SavedAsync(ApplicationBundle bundle, IReadOnlyList<Guid> affected, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();
            ApplicationBundle? current = await database.ApplicationBundles.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bundle.Id, cancellationToken).ConfigureAwait(false);

            return current is null ? EditOutcome<ApplicationBundleView>.NotFound : EditOutcome<ApplicationBundleView>.Newer(ApplicationViews.View(current));
        }

        ApplicationBundleView view = ApplicationViews.View(bundle);
        live.ApplicationBundleChanged(view);
        await views.PushAsync(live, affected, cancellationToken).ConfigureAwait(false);

        return EditOutcome<ApplicationBundleView>.Done(view);
    }
}
