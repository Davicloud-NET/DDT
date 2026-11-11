// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

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

// The console is a zip of the folder Publish-Console.ps1 writes. It runs as SYSTEM in Windows PE and as the shell of
// DDT's session in the installed Windows, so an upload needs a fresh proof of identity.
public static class SettingsConsoleEndpoints
{
    public static RouteGroupBuilder MapSettingsConsoleEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        Map(group, "/agent/console", ConsoleUpload.Windows);
        Map(group, "/agent/linux/console", ConsoleUpload.Linux);

        return group;
    }

    private static void Map(RouteGroupBuilder group, string route, ConsoleUpload console)
    {
        group.MapGet(route, async (ReleaseUploads uploads, CancellationToken cancellationToken) =>
                TypedResults.Ok(await uploads.ConsoleAsync(console, cancellationToken).ConfigureAwait(false)))
            .RequireAuthorization(DdtPolicies.Administrator);
        group.MapPut(
                route,
                ([AsParameters] SettingsCaller caller, ReleaseUploads uploads, CancellationToken cancellationToken) =>
                    UploadConsoleAsync(console, caller, uploads, cancellationToken))
            .RequireAuthorization(DdtPolicies.Administrator);
        group.MapDelete(
                route,
                ([AsParameters] SettingsCaller caller, ReleaseUploads uploads, CancellationToken cancellationToken) =>
                    RemoveConsoleAsync(console, caller, uploads, cancellationToken))
            .RequireAuthorization(DdtPolicies.Administrator);
    }

    // 404 when no console was uploaded.
    private static async Task<Results<Ok<AgentBinaryView>, NotFound>> RemoveConsoleAsync(
        ConsoleUpload console,
        SettingsCaller caller,
        ReleaseUploads uploads,
        CancellationToken cancellationToken) =>
        await uploads.RemoveConsoleAsync(console, caller.Actor, cancellationToken).ConfigureAwait(false) is { } view
            ? TypedResults.Ok(view)
            : TypedResults.NotFound();

    private static async Task<IResult> UploadConsoleAsync(
        ConsoleUpload console,
        SettingsCaller caller,
        ReleaseUploads uploads,
        CancellationToken cancellationToken)
    {
        if (uploads.ConsoleConfigured(console))
        {
            return ServerProblems.Problem(ServerMessages.SettingsConsoleConfigured.With(), StatusCodes.Status409Conflict);
        }

        if (!await caller.ReauthenticatedAsync().ConfigureAwait(false))
        {
            return SettingsEndpoints.Reauthenticate(["console"]);
        }

        if (caller.Context.Request.ContentLength > ConsoleReleaseStore.MaxBytes)
        {
            return TooLarge();
        }

        if (caller.Context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = ConsoleReleaseStore.MaxBytes;
        }

        (ReleaseUploadStatus status, AgentBinaryView? uploaded) = await uploads
            .UploadConsoleAsync(console, caller.Context.Request.Body, caller.Actor, cancellationToken)
            .ConfigureAwait(false);

        return (status, uploaded) switch
        {
            (_, { } view) => TypedResults.Ok(view),
            (ReleaseUploadStatus.TooLarge, _) => TooLarge(),
            _ => ServerProblems.Validation("package", ServerMessages.SettingsConsoleNotAPackage.With()),
        };
    }

    private static ProblemHttpResult TooLarge() =>
        ServerProblems.Problem(
            ServerMessages.SettingsConsoleTooLarge.With("max", ConsoleReleaseStore.MaxBytes / (1024 * 1024)),
            StatusCodes.Status413PayloadTooLarge);
}
