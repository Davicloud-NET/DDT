// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.BootImage;
using DDT.Contracts.Messages;
using DDT.Contracts.Packages;
using DDT.Server.Authentication;
using DDT.Server.BootImage;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Packages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace DDT.Server.Endpoints;

// Build-BootImage.ps1 asks what to put into the boot image, with an administrator's API token, and downloads it here.
public static class BootImageEndpoints
{
    public static RouteGroupBuilder MapBootImageEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ReadAsync).RequireAuthorization(DdtPolicies.Viewer);

        // A driver goes into every machine that netboots and runs as SYSTEM, so only an administrator downloads it.
        // HEAD explicitly, so a HEAD never falls through to the web UI's index page with 200.
        group.MapMethods("/drivers/{packageId:guid}/content", [HttpMethods.Get, HttpMethods.Head], ReadDriverAsync)
            .RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<Ok<BootImageView>> ReadAsync(DdtDbContext database, BootImageCatalog catalog, CancellationToken cancellationToken) =>
        TypedResults.Ok(await catalog.ViewAsync(database, cancellationToken).ConfigureAwait(false));

    // The zip as the agent downloads a run's files: tagged with its hash, so a resumed download never splices two files.
    private static async Task<Results<PhysicalFileHttpResult, NotFound, ProblemHttpResult>> ReadDriverAsync(
        Guid packageId,
        DdtDbContext database,
        ImageStore store,
        CancellationToken cancellationToken)
    {
        Package? package = await database.Packages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == packageId && p.Kind == PackageKind.Drivers && p.BootImage, cancellationToken)
            .ConfigureAwait(false);

        if (package is null)
        {
            return TypedResults.NotFound();
        }

        string path = store.ObjectPath(package.Sha256);

        if (!File.Exists(path))
        {
            return ServerProblems.Problem(ServerMessages.PackageFileMissing.With(), StatusCodes.Status404NotFound);
        }

        return TypedResults.PhysicalFile(
            path,
            "application/zip",
            fileDownloadName: $"{package.Sha256}.zip",
            entityTag: new EntityTagHeaderValue(string.Create(CultureInfo.InvariantCulture, $"\"{package.Sha256}\"")),
            enableRangeProcessing: true);
    }
}
