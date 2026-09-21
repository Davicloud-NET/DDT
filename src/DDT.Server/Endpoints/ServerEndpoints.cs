// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Contracts.Server;
using DDT.Server.Authentication;
using DDT.Server.Certificates;
using DDT.Server.Data;
using DDT.Server.Machines;
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

        // The banner that asks for a boot image rebuild goes away for everyone, so only an administrator ends it.
        group.MapDelete("/certificate/replaced-anchor", AcknowledgeReplacedAnchorAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    // Not found when Kestrel loads the certificate on its own: a PFX, a key under a password, or TLS at a proxy.
    private static Results<Ok<ServerCertificateView>, NotFound> GetCertificate([FromServices] ServerCertificates? certificates) =>
        certificates?.Describe() is { } view ? TypedResults.Ok(view) : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound>> AcknowledgeReplacedAnchorAsync(
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        TimeProvider timeProvider,
        [FromServices] ServerCertificates? certificates,
        CancellationToken cancellationToken)
    {
        if (certificates?.ReplacedAnchorSha256() is not { } sha256)
        {
            return TypedResults.NotFound();
        }

        database.AuditEvents.Add(new AuditEvent
        {
            OccurredUtc = timeProvider.GetUtcNow(),
            Action = AuditActions.CertificateAnchorAcknowledged,
            ActorUserId = Principals.UserId(user),
            ActorName = user.Identity?.Name,
            SubjectId = sha256,
            SourceAddress = context.Connection.RemoteIpAddress?.ToString(),
            Detail = $"Confirmed that every boot image was built again with DDT's root, so none pins the replaced certificate, SHA-256 {sha256}.",
        });

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        certificates.ForgetReplacedAnchor();

        return TypedResults.NoContent();
    }
}
