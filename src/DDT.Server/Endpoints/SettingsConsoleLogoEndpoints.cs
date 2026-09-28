// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Server.Authentication;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

// The logo sits on the Deployment defaults page next to the console's language, which operators may read too.
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

    private static async Task<Ok<ConsoleLogoView>> ReadAsync(ConsoleLogos logos, CancellationToken cancellationToken) =>
        TypedResults.Ok(await logos.ViewAsync(cancellationToken).ConfigureAwait(false));

    private static Results<PhysicalFileHttpResult, NotFound> ReadImage(ConsoleLogoStore logos) =>
        File.Exists(logos.Path) ? TypedResults.PhysicalFile(logos.Path, "image/png") : TypedResults.NotFound();

    private static async Task<IResult> UploadAsync(HttpContext context, ConsoleLogos logos, CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength > ConsoleLogoStore.MaxBytes)
        {
            return TooLarge();
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = ConsoleLogoStore.MaxBytes;
        }

        if (await ReadAtMostAsync(context.Request.Body, ConsoleLogoStore.MaxBytes, cancellationToken).ConfigureAwait(false) is not { } png)
        {
            return TooLarge();
        }

        (ConsoleLogoView? view, ServerMessage? refusal) = await logos
            .UploadAsync(png, SettingsEndpoints.SettingsActor(context), cancellationToken)
            .ConfigureAwait(false);

        return (view, refusal) switch
        {
            ({ } saved, _) => TypedResults.Ok(saved),
            (_, { } problem) => ServerProblems.Validation("logo", problem),
            _ => throw new UnreachableException(),
        };
    }

    private static async Task<Ok<ConsoleLogoView>> RemoveAsync(HttpContext context, ConsoleLogos logos, CancellationToken cancellationToken) =>
        TypedResults.Ok(await logos.RemoveAsync(SettingsEndpoints.SettingsActor(context), cancellationToken).ConfigureAwait(false));

    // Null when the body holds more than limit bytes.
    private static async Task<byte[]?> ReadAtMostAsync(Stream body, int limit, CancellationToken cancellationToken)
    {
        using MemoryStream content = new();
        byte[] buffer = new byte[81920];
        int read;

        while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (content.Length + read > limit)
            {
                return null;
            }

            content.Write(buffer, 0, read);
        }

        return content.ToArray();
    }

    private static ProblemHttpResult TooLarge() =>
        ServerProblems.Problem(
            ServerMessages.SettingsConsoleLogoTooLarge.With("max", ConsoleLogoStore.MaxBytes / 1024),
            StatusCodes.Status413PayloadTooLarge);
}
