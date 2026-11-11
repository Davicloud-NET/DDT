// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Server.Applications;
using DDT.Server.Authentication;
using DDT.Server.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

// The bundles of the application library, read by everyone signed in and changed by administrators.
public static class ApplicationBundleEndpoints
{
    public static RouteGroupBuilder MapApplicationBundleEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapPost("/", CreateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPut("/{id:guid}", SaveAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<ApplicationBundleView>>> ListAsync(ApplicationViews views, CancellationToken cancellationToken) =>
        TypedResults.Ok(await views.ListBundlesAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<Results<Created<ApplicationBundleView>, ValidationProblem>> CreateAsync(
        SaveApplicationBundleRequest request,
        HttpContext context,
        ApplicationBundleEditor editor,
        CancellationToken cancellationToken)
    {
        EditOutcome<ApplicationBundleView> outcome = await editor.CreateAsync(request, Actor.Of(context), cancellationToken).ConfigureAwait(false);

        return outcome.Saved is { } bundle
            ? TypedResults.Created($"/api/application-bundles/{bundle.Id:D}", bundle)
            : (outcome.Problems ?? new FieldProblems()).ToResult();
    }

    private static async Task<Results<Ok<ApplicationBundleView>, Conflict<ApplicationBundleView>, NotFound, ValidationProblem>> SaveAsync(
        Guid id,
        SaveApplicationBundleRequest request,
        HttpContext context,
        ApplicationBundleEditor editor,
        CancellationToken cancellationToken)
    {
        EditOutcome<ApplicationBundleView> outcome = await editor.SaveAsync(id, request, Actor.Of(context), cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            { Saved: { } bundle } => TypedResults.Ok(bundle),
            { Current: { } current } => TypedResults.Conflict(current),
            { Problems: { } problems } => problems.ToResult(),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid id,
        HttpContext context,
        ApplicationBundleEditor editor,
        CancellationToken cancellationToken) =>
        await editor.DeleteAsync(id, Actor.Of(context), cancellationToken).ConfigureAwait(false)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
}
