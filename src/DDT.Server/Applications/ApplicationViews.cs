// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Contracts.Packages;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Packages;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Applications;

// Applications and bundles as the Applications page sees them. What uses each is worked out from the stored rows every
// time; a library of a few hundred entries allows that.
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

        return new ApplicationRows(
            applications.ToDictionary(a => a.Id),
            [.. bundles.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ThenBy(b => b.Id)],
            files.ToDictionary(p => p.Id),
            Uses(applications, bundles));
    }

    public async Task<IReadOnlyList<ApplicationView>> ListAsync(CancellationToken cancellationToken)
    {
        ApplicationRows rows = await RowsAsync(cancellationToken).ConfigureAwait(false);

        return [.. rows.Applications.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Id).Select(a => View(a, rows))];
    }

    public async Task<IReadOnlyList<ApplicationBundleView>> ListBundlesAsync(CancellationToken cancellationToken)
    {
        ApplicationRows rows = await RowsAsync(cancellationToken).ConfigureAwait(false);

        return [.. rows.Bundles.Select(View)];
    }

    public async Task<ApplicationView> ViewAsync(Application application, CancellationToken cancellationToken) =>
        View(application, await RowsAsync(cancellationToken).ConfigureAwait(false));

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

    // No step names a bundle yet, so nothing uses one.
    public static ApplicationBundleView View(ApplicationBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        return new ApplicationBundleView(
            bundle.Id,
            bundle.Name,
            bundle.Description,
            ApplicationColumns.ReadIds(bundle.Members),
            [],
            bundle.Revision,
            bundle.UpdatedUtc,
            bundle.UpdatedByName);
    }

    // Pushes the applications whose UsedBy changed with another entry's save. Ids no longer in the library are skipped.
    public async Task PushAsync(LiveNotifier live, IEnumerable<Guid> applicationIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(applicationIds);

        HashSet<Guid> ids = [.. applicationIds];

        if (ids.Count == 0)
        {
            return;
        }

        ApplicationRows rows = await RowsAsync(cancellationToken).ConfigureAwait(false);

        foreach (Guid id in ids)
        {
            if (rows.Applications.TryGetValue(id, out Application? application))
            {
                live.ApplicationChanged(View(application, rows));
            }
        }
    }

    // Bundles first, then dependants, each by name. Sequences join here once a step names applications.
    private static Dictionary<Guid, IReadOnlyList<ApplicationUse>> Uses(List<Application> applications, List<ApplicationBundle> bundles)
    {
        IEnumerable<(Guid Used, ApplicationUse By)> byBundles = bundles.SelectMany(bundle =>
            ApplicationColumns.ReadIds(bundle.Members).Select(member => (member, new ApplicationUse(bundle.Id, bundle.Name, ApplicationUseKind.Bundle))));
        IEnumerable<(Guid Used, ApplicationUse By)> byApplications = applications.SelectMany(application =>
            ApplicationColumns.ReadIds(application.DependsOn)
                .Select(dependency => (dependency, new ApplicationUse(application.Id, application.Name, ApplicationUseKind.Application))));

        return byBundles.Concat(byApplications)
            .GroupBy(use => use.Used)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ApplicationUse>)
                [
                    .. group.Select(use => use.By).OrderBy(use => use.Kind == ApplicationUseKind.Bundle ? 0 : 1)
                        .ThenBy(use => use.Name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(use => use.Id),
                ]);
    }
}
