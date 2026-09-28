// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Endpoints;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace DDT.Server.Security;

// The second CSRF layer. On a JSON body the antiforgery middleware only records a verdict and calls the next middleware
// anyway, so a filter refuses. A request authenticated by an API token skips it, see SameOriginEndpointFilter.
public sealed class AntiforgeryEndpointFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        HttpRequest request = context.HttpContext.Request;

        if (HttpMethods.IsGet(request.Method)
            || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method)
            || HttpMethods.IsTrace(request.Method)
            || Principals.ApiTokenId(context.HttpContext.User) is not null)
        {
            return await next(context).ConfigureAwait(false);
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext).ConfigureAwait(false);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return await next(context).ConfigureAwait(false);
    }
}
