// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Packages;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Packages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Endpoints;

// Packages are uploaded through /api/images/uploads, so a proxy that allows large uploads there doesn't need a second
// rule. Packages run as SYSTEM on every machine that gets them, so only an administrator may change the library.
public static class PackageEndpoints
{
    public static RouteGroupBuilder MapPackageEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<PackageSummary>>> ListAsync(DdtDbContext database, CancellationToken cancellationToken)
    {
        List<Package> packages = await database.Packages.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<PackageSummary>>(
        [
            .. packages
                .OrderBy(p => p.Kind)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Id)
                .Select(PackageSummaries.From),
        ]);
    }

    private static async Task<Results<Ok<PackageSummary>, NotFound, ValidationProblem>> UpdateAsync(
        Guid id,
        UpdatePackageRequest request,
        HttpContext context,
        PackageLibrary library,
        CancellationToken cancellationToken) =>
        await library.UpdateAsync(id, request, Actor.Of(context), cancellationToken).ConfigureAwait(false) switch
        {
            ({ } summary, _) => TypedResults.Ok(summary),
            (_, { } problems) => problems.ToResult(),
            _ => TypedResults.NotFound(),
        };

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(
        Guid id,
        HttpContext context,
        PackageLibrary library,
        CancellationToken cancellationToken) =>
        await library.DeleteAsync(id, Actor.Of(context), cancellationToken).ConfigureAwait(false) switch
        {
            LibraryDeletion.NotFound => TypedResults.NotFound(),
            LibraryDeletion.InUse => ServerProblems.Problem(ServerMessages.PackageInUse.With(), StatusCodes.Status409Conflict),
            _ => TypedResults.NoContent(),
        };
}
