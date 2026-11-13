// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Packages;
using DDT.Server.Data;
using DDT.Server.Packages;
using DDT.Server.Sequences;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Applications;

// The applications, bundles and application files a run is planned from, by id.
public sealed record ApplicationLibrary(
    IReadOnlyDictionary<Guid, Application> Applications,
    IReadOnlyDictionary<Guid, ApplicationBundle> Bundles,
    IReadOnlyDictionary<Guid, Package> Files)
{
    public static ApplicationLibrary From(SequenceReferences references)
    {
        ArgumentNullException.ThrowIfNull(references);

        return new ApplicationLibrary(references.Applications, references.ApplicationBundles, references.ApplicationFiles);
    }

    public static async Task<ApplicationLibrary> LoadAsync(DdtDbContext database, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        List<Application> applications = await database.Applications.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        List<ApplicationBundle> bundles = await database.ApplicationBundles.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        List<Package> files = await database.Packages
            .AsNoTracking()
            .Where(p => p.Kind == PackageKind.Application)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ApplicationLibrary(applications.ToDictionary(a => a.Id), bundles.ToDictionary(b => b.Id), files.ToDictionary(p => p.Id));
    }
}
