// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Security.Claims;
using DDT.Contracts.Messages;
using DDT.Contracts.Tokens;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Security;
using DDT.Server.Tokens;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

// Every signed-in user may make tokens for themselves, with no more rights than they have. The fallback policy lets any
// signed-in user in; what a token then does is up to the policies of the endpoints it calls.
public static class ApiTokenEndpoints
{
    public static RouteGroupBuilder MapApiTokenEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListOwnAsync);
        group.MapGet("/all", ListAllAsync).RequireAuthorization(DdtPolicies.Administrator);
        // A role policy, so an account that still has to change a password it was shown cannot make a token that would
        // get it past that.
        group.MapPost("/", CreateAsync).RequireAuthorization(DdtPolicies.Viewer).RequireSession();

        // A token may revoke itself, or another of its user's: that only ever takes rights away.
        group.MapDelete("/{id:guid}", RevokeAsync);

        return group;
    }

    private static async Task<Results<Ok<IReadOnlyList<ApiTokenView>>, UnauthorizedHttpResult>> ListOwnAsync(
        ClaimsPrincipal user,
        ApiTokens tokens,
        CancellationToken cancellationToken)
    {
        if (Principals.UserId(user) is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(await tokens.ViewsAsync(userId, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<Ok<IReadOnlyList<ApiTokenView>>> ListAllAsync(ApiTokens tokens, CancellationToken cancellationToken) =>
        TypedResults.Ok(await tokens.ViewsAsync(null, cancellationToken).ConfigureAwait(false));

    private static async Task<Results<Created<CreatedApiToken>, ValidationProblem, UnauthorizedHttpResult>> CreateAsync(
        CreateApiTokenRequest request,
        HttpContext context,
        ApiTokens tokens,
        CancellationToken cancellationToken)
    {
        if (Principals.UserId(context.User) is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        (CreatedApiToken? created, FieldProblems? problems) = await tokens
            .CreateAsync(userId, context.User.Identity?.Name, request, Actor.Of(context), cancellationToken)
            .ConfigureAwait(false);

        return (created, problems) switch
        {
            ({ } token, _) => TypedResults.Created($"/api/tokens/{token.Token.Id:D}", token),
            (_, { } invalid) => invalid.ToResult(),
            _ => throw new UnreachableException(),
        };
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> RevokeAsync(
        Guid id,
        HttpContext context,
        ApiTokens tokens,
        CancellationToken cancellationToken)
    {
        if (Principals.UserId(context.User) is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        return await tokens.RevokeAsync(id, userId, context.User, Actor.Of(context), cancellationToken).ConfigureAwait(false) switch
        {
            ApiTokenRevocation.NotFound => TypedResults.NotFound(),
            ApiTokenRevocation.NotOwner => ServerProblems.Problem(ServerMessages.TokenOnlyOwnerRevokes.With(), StatusCodes.Status403Forbidden),
            _ => TypedResults.NoContent(),
        };
    }
}
