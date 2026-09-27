// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace DDT.Server.Security;

// For what changes the account itself: its password, its second factor, its external sign-in and its API tokens. A
// leaked token must not be able to lock its owner out or mint itself a successor, so only the person, signed in on the
// web, may.
public sealed class SessionOnlyEndpointFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        return Principals.ApiTokenId(context.HttpContext.User) is null
            ? next(context)
            : ValueTask.FromResult<object?>(TypedResults.Problem(
                title: "An API token cannot change the account it belongs to. Sign in on the web to do this.",
                statusCode: StatusCodes.Status403Forbidden));
    }
}

public static class SessionOnlyEndpointFilterExtensions
{
    public static TBuilder RequireSession<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddEndpointFilter(new SessionOnlyEndpointFilter());
    }
}
