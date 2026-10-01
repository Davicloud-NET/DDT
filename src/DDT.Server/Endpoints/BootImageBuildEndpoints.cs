// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text.RegularExpressions;
using DDT.Contracts.BootImage;
using DDT.Contracts.Messages;
using DDT.Server.Authentication;
using DDT.Server.BootImage;
using DDT.Server.Data;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

// The Build button: builds the boot image on a Windows server through the DDT Helper service, installs the ADK the
// same way, and switches between the builds the boot directory holds.
public static partial class BootImageBuildEndpoints
{
    private const int MaxWindowSize = 64;

    public static RouteGroupBuilder MapBootImageBuildEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/job", ReadJob).RequireAuthorization(DdtPolicies.Viewer);

        // A boot image runs as SYSTEM on every machine that netboots, and the ADK installs as SYSTEM on the server.
        group.MapPost("/build", BuildAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/adk", InstallAdkAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/current", UseAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static Results<Ok<BootImageJobLog>, NoContent> ReadJob(CurrentBootImageJob job) =>
        job.Log() is { } log ? TypedResults.Ok(log) : TypedResults.NoContent();

    private static async Task<Results<Accepted, ValidationProblem, ProblemHttpResult>> BuildAsync(
        BuildBootImageRequest request,
        [AsParameters] BootImageBuildServices services,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (request.KeyboardLayout is not null && !KeyboardLayout().IsMatch(request.KeyboardLayout))
        {
            return ServerProblems.Validation("keyboardLayout", ServerMessages.BootImageKeyboardLayout.With());
        }

        if (request.TftpWindowSize is < 1 or > MaxWindowSize)
        {
            return ServerProblems.Validation("tftpWindowSize", ServerMessages.BootImageWindowSize.With("max", MaxWindowSize));
        }

        BootImageView view = await services.Views.ViewAsync(services.Database, cancellationToken).ConfigureAwait(false);

        if (Refusal(view.Builder, needsAdk: true) is { } refusal)
        {
            return refusal;
        }

        HelperRequest build = new()
        {
            Kind = HelperRequest.Build,
            Name = services.TimeProvider.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture),
            ServerUrl = view.Builder.ServerUrl,
            KeyboardLayout = request.KeyboardLayout,
            SkipPowerShell = request.SkipPowerShell,
            TftpWindowSize = request.TftpWindowSize,
            Drivers = [.. view.Drivers.Select(driver => new HelperDriver(driver.PackageId, driver.Name, driver.Sha256))],
            DriverSetHash = view.DriverSetHash,
        };

        return await StartAsync(BootImageJobKind.Build, build, AuditActions.BootImageBuildStarted, services, context).ConfigureAwait(false) is { } busy
            ? busy
            : TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<Accepted, ProblemHttpResult>> InstallAdkAsync(
        [AsParameters] BootImageBuildServices services,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        BootImageView view = await services.Views.ViewAsync(services.Database, cancellationToken).ConfigureAwait(false);

        if (Refusal(view.Builder, needsAdk: false) is { } refusal)
        {
            return refusal;
        }

        HelperRequest install = new() { Kind = HelperRequest.InstallAdk };

        return await StartAsync(BootImageJobKind.InstallAdk, install, AuditActions.AdkInstallStarted, services, context).ConfigureAwait(false) is { } busy
            ? busy
            : TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<Ok<BootImageView>, ProblemHttpResult>> UseAsync(
        UseBootImageBuildRequest request,
        [AsParameters] BootImageBuildServices services,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!services.Catalog.Use(request.Name))
        {
            return ServerProblems.Problem(ServerMessages.BootImageNoSuchBuild.With(), StatusCodes.Status404NotFound);
        }

        services.Database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.BootImageBuildChosen,
            request.Name,
            Actor.Of(context),
            services.TimeProvider.GetUtcNow(),
            request.Name is null ? "Serves the files in the boot directory itself." : $"Serves the build {request.Name}."));
        await services.Database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        BootImageView view = await services.Views.ViewAsync(services.Database, cancellationToken).ConfigureAwait(false);
        services.Live.BootImageChanged(view);

        return TypedResults.Ok(view);
    }

    private static ProblemHttpResult? Refusal(BootImageBuilder builder, bool needsAdk)
    {
        if (!builder.Available)
        {
            return ServerProblems.Problem(ServerMessages.BootImageNoHelper.With(), StatusCodes.Status409Conflict);
        }

        if (!needsAdk || builder.Adk is not { } adk)
        {
            return null;
        }

        return !adk.Installed
            ? ServerProblems.Problem(ServerMessages.BootImageNoAdk.With(), StatusCodes.Status409Conflict)
            : !adk.Supported
                ? ServerProblems.Problem(
                    ServerMessages.BootImageOldAdk.With("version", adk.Version ?? "unknown", "oldest", InstalledAdk.OldestAddOn.ToString()),
                    StatusCodes.Status409Conflict)
                : null;
    }

    // Returns null when the job started, with its audit row saved, or the refusal while another runs.
    private static async Task<ProblemHttpResult?> StartAsync(
        BootImageJobKind kind,
        HelperRequest request,
        string action,
        BootImageBuildServices services,
        HttpContext context)
    {
        Actor actor = SettingsEndpoints.SettingsActor(context);

        if (!services.Jobs.TryStart(kind, request, actor.Name ?? "unknown"))
        {
            return ServerProblems.Problem(ServerMessages.BootImageBusy.With(), StatusCodes.Status409Conflict);
        }

        services.Database.AuditEvents.Add(AuditEvents.Create(action, request.Name, actor, services.TimeProvider.GetUtcNow(), null));
        await services.Database.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

        return null;
    }

    [GeneratedRegex("^[0-9A-Fa-f]{4}:[0-9A-Fa-f]{8}$")]
    private static partial Regex KeyboardLayout();
}
