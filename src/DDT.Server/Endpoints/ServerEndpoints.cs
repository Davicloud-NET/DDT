// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Server;
using DDT.Server.Authentication;
using DDT.Server.Certificates;
using DDT.Server.Data;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

public static class ServerEndpoints
{
    public static RouteGroupBuilder MapServerEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/certificate", GetCertificate).RequireAuthorization(DdtPolicies.Viewer);

        // Dismissing the banner that asks for a boot image rebuild hides it for everyone, so only an administrator may.
        group.MapDelete("/certificate/replaced-anchor", AcknowledgeReplacedAnchorAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    // Returns 404 when Kestrel loads the certificate by itself. That's the case for a PFX, a password-protected key, or
    // TLS at a proxy.
    private static Results<Ok<ServerCertificateView>, NotFound> GetCertificate([FromServices] ServerCertificates? certificates) =>
        certificates?.Describe() is { } view ? TypedResults.Ok(view) : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound>> AcknowledgeReplacedAnchorAsync(
        HttpContext context,
        CertificateChanges changes,
        CancellationToken cancellationToken) =>
        await changes.AcknowledgeReplacedAnchorAsync(Actor.Of(context), cancellationToken).ConfigureAwait(false)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
}
