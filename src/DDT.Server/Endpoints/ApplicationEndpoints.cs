// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Contracts.Messages;
using DDT.Server.Applications;
using DDT.Server.Authentication;
using DDT.Server.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

// The application library. Applications install as SYSTEM on every machine that gets them, so only an administrator
// changes the library. Their files are uploaded through /api/images/uploads with the kind Application.
public static class ApplicationEndpoints
{
    public static RouteGroupBuilder MapApplicationEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapPost("/", CreateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPut("/{id:guid}", SaveAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<ApplicationView>>> ListAsync(ApplicationViews views, CancellationToken cancellationToken) =>
        TypedResults.Ok(await views.ListAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<Results<Created<ApplicationView>, ValidationProblem>> CreateAsync(
        SaveApplicationRequest request,
        HttpContext context,
        ApplicationEditor editor,
        CancellationToken cancellationToken)
    {
        EditOutcome<ApplicationView> outcome = await editor.CreateAsync(request, Actor.Of(context), cancellationToken).ConfigureAwait(false);

        return outcome.Saved is { } application
            ? TypedResults.Created($"/api/applications/{application.Id:D}", application)
            : (outcome.Problems ?? new FieldProblems()).ToResult();
    }

    // A save on an older revision gets the current application, and the page decides what to do with it.
    private static async Task<Results<Ok<ApplicationView>, Conflict<ApplicationView>, NotFound, ValidationProblem, ProblemHttpResult>> SaveAsync(
        Guid id,
        SaveApplicationRequest request,
        HttpContext context,
        ApplicationEditor editor,
        CancellationToken cancellationToken)
    {
        EditOutcome<ApplicationView> outcome = await editor.SaveAsync(id, request, Actor.Of(context), cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            { Saved: { } application } => TypedResults.Ok(application),
            { Current: { } current } => TypedResults.Conflict(current),
            { Problems: { } problems } => problems.ToResult(),
            { Refusal: { } refusal } => ServerProblems.Problem(refusal, StatusCodes.Status409Conflict),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(
        Guid id,
        HttpContext context,
        ApplicationEditor editor,
        CancellationToken cancellationToken)
    {
        (bool found, ServerMessage? refusal) = await editor.DeleteAsync(id, Actor.Of(context), cancellationToken).ConfigureAwait(false);

        if (!found)
        {
            return TypedResults.NotFound();
        }

        return refusal is null ? TypedResults.NoContent() : ServerProblems.Problem(refusal, StatusCodes.Status409Conflict);
    }
}
