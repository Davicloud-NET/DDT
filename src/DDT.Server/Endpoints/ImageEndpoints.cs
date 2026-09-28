// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;
using DDT.Contracts.Messages;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Images;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Endpoints;

public static class ImageEndpoints
{
    public static RouteGroupBuilder MapImageEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Viewer);

        // An image runs on every machine it is deployed to, so only an administrator changes the library.
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapGroup("/uploads").MapImageUploadEndpoints();

        return group;
    }

    // A library holds tens of images, and sorting here gives every database the same order.
    private static async Task<Ok<IReadOnlyList<ImageSummary>>> ListAsync(DdtDbContext database, CancellationToken cancellationToken)
    {
        List<Image> images = await database.Images.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<ImageSummary>>(
        [
            .. images
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.WimIndex)
                .ThenBy(i => i.Id)
                .Select(ImageSummaries.From),
        ]);
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(
        Guid id,
        HttpContext context,
        ImageLibrary library,
        CancellationToken cancellationToken) =>
        await library.DeleteAsync(id, Actor.Of(context), cancellationToken).ConfigureAwait(false) switch
        {
            LibraryDeletion.NotFound => TypedResults.NotFound(),
            LibraryDeletion.InUse => ServerProblems.Problem(ServerMessages.ImageInUse.With(), StatusCodes.Status409Conflict),
            _ => TypedResults.NoContent(),
        };
}
