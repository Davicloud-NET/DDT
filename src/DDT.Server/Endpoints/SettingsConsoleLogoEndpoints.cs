// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Endpoints;

// The logo the console at the machine shows, on the Deployment defaults page next to the console's language, which
// operators may read too. Machines take it at their next registration.
public static class SettingsConsoleLogoEndpoints
{
    public static RouteGroupBuilder MapSettingsConsoleLogoEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/console-logo", ReadAsync).RequireAuthorization(DdtPolicies.Operator);
        group.MapGet("/console-logo/image", ReadImage).RequireAuthorization(DdtPolicies.Operator);
        group.MapPut("/console-logo", UploadAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapDelete("/console-logo", RemoveAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<Ok<ConsoleLogoView>> ReadAsync(ConsoleLogoStore logos, DdtDbContext database, CancellationToken cancellationToken) =>
        TypedResults.Ok(await ViewAsync(logos, database, cancellationToken).ConfigureAwait(false));

    private static Results<PhysicalFileHttpResult, NotFound> ReadImage(ConsoleLogoStore logos) =>
        File.Exists(logos.Path) ? TypedResults.PhysicalFile(logos.Path, "image/png") : TypedResults.NotFound();

    private static async Task<IResult> UploadAsync(
        HttpContext context,
        ClaimsPrincipal user,
        ConsoleLogoStore logos,
        DdtDbContext database,
        TimeProvider timeProvider,
        LiveNotifier live,
        CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength > ConsoleLogoStore.MaxBytes)
        {
            return TooLarge();
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = ConsoleLogoStore.MaxBytes;
        }

        using MemoryStream body = new();
        byte[] buffer = new byte[81920];
        int read;

        while ((read = await context.Request.Body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (body.Length + read > ConsoleLogoStore.MaxBytes)
            {
                return TooLarge();
            }

            body.Write(buffer, 0, read);
        }

        byte[] png = body.ToArray();

        if (ConsoleLogoStore.Describe(png) is not { } logo)
        {
            return ServerProblems.Validation("logo", ServerMessages.SettingsConsoleLogoNotPng.With());
        }

        if (logo.Width > ConsoleLogoStore.MaxDimension || logo.Height > ConsoleLogoStore.MaxDimension)
        {
            return ServerProblems.Validation(
                "logo",
                ServerMessages.SettingsConsoleLogoDimensions.With("width", logo.Width, "height", logo.Height, "max", ConsoleLogoStore.MaxDimension));
        }

        await logos.SaveAsync(png, cancellationToken).ConfigureAwait(false);
        await AuditAsync(
            database,
            context,
            user,
            timeProvider,
            AuditActions.ConsoleLogoUploaded,
            logo.Sha256,
            $"Uploaded the console's logo with SHA-256 {logo.Sha256}, {logo.Width} by {logo.Height} pixels, {logo.Size} bytes. Machines show it from their next registration.",
            cancellationToken).ConfigureAwait(false);

        ConsoleLogoView view = await ViewAsync(logos, database, cancellationToken).ConfigureAwait(false);
        live.ConsoleLogoChanged(view);

        return TypedResults.Ok(view);
    }

    private static async Task<IResult> RemoveAsync(
        HttpContext context,
        ClaimsPrincipal user,
        ConsoleLogoStore logos,
        DdtDbContext database,
        TimeProvider timeProvider,
        LiveNotifier live,
        CancellationToken cancellationToken)
    {
        if (await logos.CurrentAsync(cancellationToken).ConfigureAwait(false) is { } logo)
        {
            logos.Delete();
            await AuditAsync(
                database,
                context,
                user,
                timeProvider,
                AuditActions.ConsoleLogoRemoved,
                logo.Sha256,
                $"Removed the console's logo with SHA-256 {logo.Sha256}. Machines show none from their next registration.",
                cancellationToken).ConfigureAwait(false);
        }

        ConsoleLogoView view = await ViewAsync(logos, database, cancellationToken).ConfigureAwait(false);
        live.ConsoleLogoChanged(view);

        return TypedResults.Ok(view);
    }

    // Who uploaded the logo and when come from its latest upload's audit row.
    private static async Task<ConsoleLogoView> ViewAsync(ConsoleLogoStore logos, DdtDbContext database, CancellationToken cancellationToken)
    {
        if (await logos.CurrentAsync(cancellationToken).ConfigureAwait(false) is not { } logo)
        {
            return new ConsoleLogoView(null, null, null, null, null, null);
        }

        AuditEvent? upload = await database.AuditEvents
            .AsNoTracking()
            .Where(audit => audit.Action == AuditActions.ConsoleLogoUploaded && audit.SubjectId == logo.Sha256)
            .OrderByDescending(audit => audit.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ConsoleLogoView(logo.Sha256, logo.Size, logo.Width, logo.Height, upload?.OccurredUtc, upload?.ActorName);
    }

    private static async Task AuditAsync(
        DdtDbContext database,
        HttpContext context,
        ClaimsPrincipal user,
        TimeProvider timeProvider,
        string action,
        string sha256,
        string detail,
        CancellationToken cancellationToken)
    {
        database.AuditEvents.Add(new AuditEvent
        {
            OccurredUtc = timeProvider.GetUtcNow(),
            Action = action,
            ActorUserId = Principals.UserId(user),
            ActorName = Principals.ActorName(user),
            SubjectId = sha256,
            SourceAddress = context.Connection.RemoteIpAddress?.ToString(),
            Detail = detail,
        });

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ProblemHttpResult TooLarge() =>
        ServerProblems.Problem(
            ServerMessages.SettingsConsoleLogoTooLarge.With("max", ConsoleLogoStore.MaxBytes / 1024),
            StatusCodes.Status413PayloadTooLarge);
}
