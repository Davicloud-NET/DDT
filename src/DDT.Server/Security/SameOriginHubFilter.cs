// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.AspNetCore.Http;

namespace DDT.Server.Security;

// A page on another port of this host, or on a sibling host under the same domain, is same-site: it could open the hub
// WebSocket with the session cookie, skip negotiation and read every event. Browsers always send Origin on that
// handshake and no Sec-Fetch-Site, so Origin decides, for GET too, as the upgrade and long polling are GETs.
public sealed class SameOriginHubFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        HttpRequest request = context.HttpContext.Request;
        string? fetchSite = request.Headers["Sec-Fetch-Site"];
        string origin = request.Headers.Origin.ToString();

        // No Origin and no Sec-Fetch-Site is a client that is not a browser.
        bool sameOrigin = fetchSite is not null
            ? fetchSite == "same-origin"
            : origin.Length == 0 || string.Equals(origin, $"{request.Scheme}://{request.Host.Value}", StringComparison.OrdinalIgnoreCase);

        return sameOrigin
            ? next(context)
            : ValueTask.FromResult<object?>(Results.StatusCode(StatusCodes.Status403Forbidden));
    }
}
