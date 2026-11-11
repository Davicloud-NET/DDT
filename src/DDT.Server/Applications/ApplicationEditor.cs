// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Contracts.Messages;
using DDT.Server.Data;
using DDT.Server.Endpoints;
using DDT.Server.Images;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Packages;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Applications;

// Saves the applications in the library, each change audited and pushed. Every write holds the library lock: files and
// rows change together, and a name stays unique across applications and bundles.
internal sealed class ApplicationEditor(
    DdtDbContext database,
    ImageStore store,
    ApplicationViews views,
    LiveNotifier live,
    TimeProvider timeProvider)
{
    public async Task<EditOutcome<ApplicationView>> CreateAsync(SaveApplicationRequest request, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        await using SemaphoreHold hold = await store.LibraryLock.EnterAsync(cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = timeProvider.GetUtcNow();
        Guid id = Guid.CreateVersion7(now);
        ApplicationRows rows = await views.RowsAsync(cancellationToken).ConfigureAwait(false);
        (ApplicationFields? fields, FieldProblems? problems) = ApplicationLibraryChecks.Check(request, id, isNew: true, rows, store);

        if (fields is null)
        {
            return EditOutcome<ApplicationView>.Invalid(problems ?? new FieldProblems());
        }

        Application application = new() { Id = id, Name = fields.Name, NormalizedName = "", Revision = 1, CreatedUtc = now };
        fields.ApplyTo(application);
        Stamp(application, actor, now);
        database.Applications.Add(application);
        Audit(AuditActions.ApplicationCreated, application, actor, now, $"Created the application {application.Name}: {ApplicationChanges.Created(fields, rows)}.");

        return await SavedAsync(application, null, fields.DependsOn, cancellationToken).ConfigureAwait(false);
    }

    // A save names the revision it was based on. If the application has a newer one, the save gets the current one.
    public async Task<EditOutcome<ApplicationView>> SaveAsync(Guid id, SaveApplicationRequest request, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        await using SemaphoreHold hold = await store.LibraryLock.EnterAsync(cancellationToken).ConfigureAwait(false);

        Application? application = await database.Applications.FirstOrDefaultAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false);

        if (application is null)
        {
            return EditOutcome<ApplicationView>.NotFound;
        }

        ApplicationRows rows = await views.RowsAsync(cancellationToken).ConfigureAwait(false);

        if (request.Revision != application.Revision)
        {
            return EditOutcome<ApplicationView>.Newer(ApplicationViews.View(application, rows));
        }

        (ApplicationFields? fields, FieldProblems? problems) = ApplicationLibraryChecks.Check(request, id, isNew: false, rows, store);

        if (fields is null)
        {
            return EditOutcome<ApplicationView>.Invalid(problems ?? new FieldProblems());
        }

        List<string> changes = ApplicationChanges.Of(application, fields, rows);

        // Saving what's already stored changes nothing, so nothing is recorded.
        if (changes.Count == 0)
        {
            return EditOutcome<ApplicationView>.Done(ApplicationViews.View(application, rows));
        }

        (Package? released, bool inUse) = application.PackageId is { } previous && previous != fields.PackageId
            ? await ApplicationFilesRelease.ReleaseAsync(database, previous, id, cancellationToken).ConfigureAwait(false)
            : (null, false);

        if (inUse)
        {
            return EditOutcome<ApplicationView>.Refused(ServerMessages.ApplicationFilesInUse.With());
        }

        IReadOnlyList<Guid> before = ApplicationColumns.ReadIds(application.DependsOn);
        bool renamed = application.Name != fields.Name;
        DateTimeOffset now = timeProvider.GetUtcNow();

        fields.ApplyTo(application);
        application.Revision++;
        Stamp(application, actor, now);
        Audit(AuditActions.ApplicationChanged, application, actor, now, $"Changed the application {application.Name}: {string.Join(", ", changes)}.");

        // A dependency shows its dependants by name, so a rename changes every one of them.
        IEnumerable<Guid> affected = renamed ? before.Union(fields.DependsOn) : before.Except(fields.DependsOn).Union(fields.DependsOn.Except(before));

        return await SavedAsync(application, released, [.. affected], cancellationToken).ConfigureAwait(false);
    }

    // Refused while a bundle, another application or a sequence names it. Found is false if it doesn't exist.
    public async Task<(bool Found, ServerMessage? Refusal)> DeleteAsync(Guid id, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        await using SemaphoreHold hold = await store.LibraryLock.EnterAsync(cancellationToken).ConfigureAwait(false);

        Application? application = await database.Applications.FirstOrDefaultAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false);

        if (application is null)
        {
            return (false, null);
        }

        ApplicationRows rows = await views.RowsAsync(cancellationToken).ConfigureAwait(false);

        if (rows.UsesOf(id) is { Count: > 0 } uses)
        {
            return (true, ServerMessages.ApplicationInUse.With("users", string.Join(", ", uses.Select(use => use.Name))));
        }

        Package? released = null;

        if (application.PackageId is { } packageId)
        {
            (released, bool inUse) = await ApplicationFilesRelease.ReleaseAsync(database, packageId, id, cancellationToken).ConfigureAwait(false);

            if (inUse)
            {
                return (true, ServerMessages.ApplicationFilesInUse.With());
            }
        }

        database.Applications.Remove(application);
        Audit(
            AuditActions.ApplicationDeleted,
            application,
            actor,
            timeProvider.GetUtcNow(),
            $"Deleted the application {application.Name}, files {(released is null ? "kept" : "removed")}.");

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (true, ServerMessages.ApplicationChangedMeanwhile.With());
        }

        await DeleteFileAsync(released).ConfigureAwait(false);
        live.ApplicationsRemoved([id]);
        await views.PushAsync(live, ApplicationColumns.ReadIds(application.DependsOn), cancellationToken).ConfigureAwait(false);

        return (true, null);
    }

    private void Audit(string action, Application application, Actor actor, DateTimeOffset now, string detail) =>
        database.AuditEvents.Add(AuditEvents.Create(action, application.Id.ToString("D"), actor, now, detail));

    private static void Stamp(Application application, Actor actor, DateTimeOffset now)
    {
        application.UpdatedUtc = now;
        application.UpdatedByUserId = actor.UserId;
        application.UpdatedByName = actor.Name;
    }

    // Saves and pushes the application, then the applications whose UsedBy changed with it. Released files are deleted
    // once the save no longer names them.
    private async Task<EditOutcome<ApplicationView>> SavedAsync(
        Application application,
        Package? released,
        IReadOnlyList<Guid> affected,
        CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();
            Application? current = await database.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == application.Id, cancellationToken).ConfigureAwait(false);

            return current is null
                ? EditOutcome<ApplicationView>.NotFound
                : EditOutcome<ApplicationView>.Newer(await views.ViewAsync(current, cancellationToken).ConfigureAwait(false));
        }

        await DeleteFileAsync(released).ConfigureAwait(false);

        ApplicationView view = await views.ViewAsync(application, cancellationToken).ConfigureAwait(false);
        live.ApplicationChanged(view);
        await views.PushAsync(live, affected, cancellationToken).ConfigureAwait(false);

        return EditOutcome<ApplicationView>.Done(view);
    }

    private async Task DeleteFileAsync(Package? released)
    {
        if (released is not null)
        {
            await store.DeleteObjectIfUnreferencedAsync(database, released.Sha256, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
