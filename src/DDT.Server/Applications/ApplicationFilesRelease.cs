// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Packages;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Packages;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Applications;

// Removes an application's files when the last application that names them lets go. Call with the library lock held;
// the row goes with the caller's save, the stored file after it.
internal static class ApplicationFilesRelease
{
    // Removed is null when another application still names the files. InUse is true when a run still downloads them,
    // and then nothing is removed.
    public static async Task<(Package? Removed, bool InUse)> ReleaseAsync(
        DdtDbContext database,
        Guid packageId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        if (await database.Applications.AnyAsync(a => a.PackageId == packageId && a.Id != applicationId, cancellationToken).ConfigureAwait(false))
        {
            return (null, false);
        }

        Package? package = await database.Packages
            .FirstOrDefaultAsync(p => p.Id == packageId && p.Kind == PackageKind.Application, cancellationToken)
            .ConfigureAwait(false);

        if (package is null)
        {
            return (null, false);
        }

        if (await InUseAsync(database, packageId, cancellationToken).ConfigureAwait(false))
        {
            return (null, true);
        }

        database.Packages.Remove(package);

        return (package, false);
    }

    // A run's artifact names the files it downloads by their package.
    public static Task<bool> InUseAsync(DdtDbContext database, Guid packageId, CancellationToken cancellationToken) =>
        ActiveArtifacts.Of(database).AnyAsync(a => a.Kind != ArtifactKind.Image && a.SourceId == packageId, cancellationToken);
}
