// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Endpoints;
using Microsoft.AspNetCore.Http;

namespace DDT.Server.Security;

// The first CSRF layer: Sec-Fetch-Site comes from the browser, and page script cannot forge it, so a cross-site POST is
// refused before any handler runs. A request authenticated by an API token skips both layers: only its Authorization
// header authenticated it, which a page on another site cannot make a browser send, as DDT allows no cross-origin
// request that needs a preflight.
public sealed class SameOriginEndpointFilter : IEndpointFilter
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

        string? fetchSite = request.Headers["Sec-Fetch-Site"];

        if (fetchSite is "same-origin" or "none")
        {
            return await next(context).ConfigureAwait(false);
        }

        if (fetchSite is null)
        {
            string origin = request.Headers.Origin.ToString();
            string expected = $"{request.Scheme}://{request.Host.Value}";

            if (origin.Length == 0 || string.Equals(origin, expected, StringComparison.OrdinalIgnoreCase))
            {
                return await next(context).ConfigureAwait(false);
            }
        }

        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }
}
