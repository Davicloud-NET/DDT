// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Contracts.Packages;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Packages;
using DDT.Server.Sequences;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Applications;

// Applications and bundles as the Applications page sees them. What uses each is worked out from the stored rows and
// sequences every time; a library of a few hundred entries allows that.
public sealed class ApplicationViews(DdtDbContext database)
{
    public async Task<ApplicationRows> RowsAsync(CancellationToken cancellationToken)
    {
        List<Application> applications = await database.Applications.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        List<ApplicationBundle> bundles = await database.ApplicationBundles.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        List<Package> files = await database.Packages
            .AsNoTracking()
            .Where(p => p.Kind == PackageKind.Application)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        List<TaskSequence> sequences = await database.TaskSequences.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        return new ApplicationRows(
            applications.ToDictionary(a => a.Id),
            [.. bundles.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ThenBy(b => b.Id)],
            files.ToDictionary(p => p.Id),
            ApplicationUses.Of(applications, bundles, sequences));
    }

    public async Task<IReadOnlyList<ApplicationView>> ListAsync(CancellationToken cancellationToken)
    {
        ApplicationRows rows = await RowsAsync(cancellationToken).ConfigureAwait(false);

        return [.. rows.Applications.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Id).Select(a => View(a, rows))];
    }

    public async Task<IReadOnlyList<ApplicationBundleView>> ListBundlesAsync(CancellationToken cancellationToken)
    {
        ApplicationRows rows = await RowsAsync(cancellationToken).ConfigureAwait(false);

        return [.. rows.Bundles.Select(bundle => View(bundle, rows))];
    }

    public async Task<ApplicationView> ViewAsync(Application application, CancellationToken cancellationToken) =>
        View(application, await RowsAsync(cancellationToken).ConfigureAwait(false));

    public async Task<ApplicationBundleView> ViewAsync(ApplicationBundle bundle, CancellationToken cancellationToken) =>
        View(bundle, await RowsAsync(cancellationToken).ConfigureAwait(false));

    public static ApplicationView View(Application application, ApplicationRows rows)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(rows);

        ApplicationFiles? files = application.PackageId is { } packageId && rows.Files.TryGetValue(packageId, out Package? package)
            ? ApplicationUploads.Files(package)
            : null;

        return new ApplicationView(
            application.Id,
            application.Name,
            application.Description,
            application.Publisher,
            application.Version,
            application.Kind,
            files,
            application.Command,
            ApplicationColumns.ReadCodes(application.SuccessExitCodes),
            ApplicationColumns.ReadCodes(application.RestartExitCodes),
            ApplicationColumns.ReadDetection(application.Detection),
            ApplicationColumns.ReadIds(application.DependsOn),
            application.WingetId,
            application.WingetVersion,
            application.TimeoutMinutes,
            application.ContinueOnFailure,
            rows.UsesOf(application.Id),
            application.Revision,
            application.UpdatedUtc,
            application.UpdatedByName);
    }

    public static ApplicationBundleView View(ApplicationBundle bundle, ApplicationRows rows)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(rows);

        return new ApplicationBundleView(
            bundle.Id,
            bundle.Name,
            bundle.Description,
            ApplicationColumns.ReadIds(bundle.Members),
            rows.UsesOf(bundle.Id),
            bundle.Revision,
            bundle.UpdatedUtc,
            bundle.UpdatedByName);
    }

    // Pushes the applications and bundles whose UsedBy changed with another entry's save or a sequence's. Ids no longer
    // in the library are skipped.
    public async Task PushAsync(LiveNotifier live, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(ids);

        HashSet<Guid> changed = [.. ids];

        if (changed.Count == 0)
        {
            return;
        }

        ApplicationRows rows = await RowsAsync(cancellationToken).ConfigureAwait(false);

        foreach (Guid id in changed)
        {
            if (rows.Applications.TryGetValue(id, out Application? application))
            {
                live.ApplicationChanged(View(application, rows));
            }
        }

        foreach (ApplicationBundle bundle in rows.Bundles.Where(bundle => changed.Contains(bundle.Id)))
        {
            live.ApplicationBundleChanged(View(bundle, rows));
        }
    }
}
