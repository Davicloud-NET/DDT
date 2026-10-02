// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.BootImage;
using DDT.Contracts.Messages;
using DDT.Server.Authentication;
using DDT.Server.BootImage;
using DDT.Server.Certificates;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Endpoints;

// The boot image from another PC: an administrator downloads the builder, runs it on a Windows PC with the ADK, and it
// uploads what it built. For a Linux server, and for a Windows server without the ADK.
public static class BootImageBuilderEndpoints
{
    public static RouteGroupBuilder MapBootImageBuilderEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/builder", DownloadAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    // Outside the API's group: a builder has no session and sends no origin. Its token is all it has.
    public static IEndpointRouteBuilder MapBootImageUpload(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPut("/api/boot-image", UploadAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.AgentRelease);

        // The builder asks before it builds, which takes minutes
        app.MapGet("/api/boot-image/builder", CheckAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.AgentRelease);

        return app;
    }

    // A boot image runs as SYSTEM on every machine that netboots, so the builder takes a fresh proof of identity, like
    // an agent upload.
    private static async Task<IResult> DownloadAsync(
        [AsParameters] SettingsCaller caller,
        [AsParameters] BootImageBuilderServices services,
        CancellationToken cancellationToken)
    {
        if (!services.Package.Available)
        {
            return ServerProblems.Problem(ServerMessages.BootImageNoBuilder.With(), StatusCodes.Status409Conflict);
        }

        // Registered only where DDT makes its own certificate
        if (services.Services.GetService<ServerCertificates>()?.RootCertificatePem is not { } root)
        {
            return ServerProblems.Problem(ServerMessages.BootImageNoRoot.With(), StatusCodes.Status409Conflict);
        }

        if (!await caller.ReauthenticatedAsync().ConfigureAwait(false))
        {
            return SettingsEndpoints.Reauthenticate(["builder"]);
        }

        BootImageView view = await services.Views.ViewAsync(services.Database, cancellationToken).ConfigureAwait(false);
        (string token, BuilderToken payload) = services.Tokens.Issue(caller.Actor.Name ?? "unknown");
        HelperDriverList drivers = new(view.DriverSetHash, [.. view.Drivers.Select(driver => new HelperDriver(driver.PackageId, driver.Name, driver.Sha256))]);

        FileStream zip = await services.Package
            .CreateAsync(new BuilderFile(view.Builder.ServerUrl, token, payload.ExpiresUtc), root, drivers, cancellationToken)
            .ConfigureAwait(false);

        services.Database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.BootImageBuilderDownloaded,
            payload.Id.ToString("N"),
            caller.Actor,
            services.TimeProvider.GetUtcNow(),
            $"For {view.Builder.ServerUrl}, with {drivers.Drivers.Count} driver packages."));
        await services.Database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.File(zip, "application/zip", "ddt-boot-image-builder.zip");
    }

    private static async Task<IResult> UploadAsync(
        HttpContext context,
        [AsParameters] BootImageBuilderServices services,
        BootImageUploads uploads,
        CancellationToken cancellationToken)
    {
        if (await UsableAsync(context, services, cancellationToken).ConfigureAwait(false) is not { } token)
        {
            return Refused();
        }

        if (context.Request.ContentLength > BootImageUploads.MaxBytes)
        {
            return TooLarge();
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = BootImageUploads.MaxBytes;
        }

        (string? name, ServerMessage? refusal) = await uploads.ReceiveAsync(context.Request.Body, token, cancellationToken).ConfigureAwait(false);

        if (refusal is not null)
        {
            return refusal.Code == ServerMessages.BootImageBusy.Code
                ? ServerProblems.Problem(refusal, StatusCodes.Status409Conflict)
                : refusal.Code == ServerMessages.BootImageUploadTooLarge.Code ? TooLarge() : ServerProblems.Problem(refusal, StatusCodes.Status400BadRequest);
        }

        services.Database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.BootImageUploaded,
            token.Id.ToString("N"),
            new Actor(null, $"{token.IssuedBy} (builder)", context.Connection.RemoteIpAddress?.ToString()),
            services.TimeProvider.GetUtcNow(),
            $"Serves the build {name}."));
        await services.Database.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> CheckAsync(HttpContext context, [AsParameters] BootImageBuilderServices services, CancellationToken cancellationToken) =>
        await UsableAsync(context, services, cancellationToken).ConfigureAwait(false) is null ? Refused() : TypedResults.NoContent();

    // The request's token, if it is one of this server's that has not uploaded yet. The audit log says whether it has:
    // every host of the server reads it.
    private static async Task<BuilderToken?> UsableAsync(HttpContext context, BootImageBuilderServices services, CancellationToken cancellationToken)
    {
        if (services.Tokens.Validate(context.Request.Headers[BuilderTokens.HeaderName]) is not { } token)
        {
            return null;
        }

        string id = token.Id.ToString("N");
        bool used = await services.Database.AuditEvents
            .AnyAsync(audit => audit.Action == AuditActions.BootImageUploaded && audit.SubjectId == id, cancellationToken)
            .ConfigureAwait(false);

        return used ? null : token;
    }

    private static IResult Refused() => ServerProblems.Problem(ServerMessages.BootImageUploadToken.With(), StatusCodes.Status401Unauthorized);

    private static IResult TooLarge() =>
        ServerProblems.Problem(
            ServerMessages.BootImageUploadTooLarge.With("max", BootImageUploads.MaxBytes / (1024 * 1024)),
            StatusCodes.Status413PayloadTooLarge);
}
