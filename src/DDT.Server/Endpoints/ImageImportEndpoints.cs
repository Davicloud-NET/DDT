// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;
using DDT.Contracts.Import;
using DDT.Contracts.Machines;
using DDT.Contracts.Messages;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Import;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

// Imports from the server's own disks: a WIM, ESD or ISO, and an MDT deployment share's images and drivers. A browser
// upload of five gigabytes is the slowest part of a first deployment, and an MDT shop has the files there already.
public static class ImageImportEndpoints
{
    public static RouteGroupBuilder MapImageImportEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", Read).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/", ImportFilesAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/mdt/inspect", InspectShare).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/mdt", ImportShareAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static Ok<ImportSources> Read(ImportFolders folders, CurrentImport current) =>
        TypedResults.Ok(new ImportSources(folders.Folders(), folders.Files(), folders.Shares(), current.Status));

    private static async Task<Results<Accepted, ProblemHttpResult>> ImportFilesAsync(
        ImportFilesRequest request,
        [AsParameters] ImageImportServices services,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        List<ImportItem> items = [];

        foreach (string path in request.Paths ?? [])
        {
            if (services.Folders.Allowed(path) is not { } allowed || !ImportFolders.IsImage(allowed))
            {
                return ServerProblems.Problem(ServerMessages.ImportNotAllowed.With("path", path), StatusCodes.Status400BadRequest);
            }

            if (!File.Exists(allowed))
            {
                return ServerProblems.Problem(ServerMessages.ImportNoSuchFile.With("path", path), StatusCodes.Status404NotFound);
            }

            items.Add(new ImportItem(UploadKind.Image, Path.GetFileName(allowed), allowed, []));
        }

        return await StartAsync(items, $"{items.Count} files.", services, context, cancellationToken).ConfigureAwait(false);
    }

    private static Results<Ok<MdtShareView>, ProblemHttpResult> InspectShare(InspectMdtShareRequest request, ImportFolders folders) =>
        Share(request.Path, folders) is { } share
            ? TypedResults.Ok(MdtShare.Read(share))
            : ServerProblems.Problem(ServerMessages.ImportNotAShare.With("path", request.Path ?? string.Empty), StatusCodes.Status400BadRequest);

    private static async Task<Results<Accepted, ProblemHttpResult>> ImportShareAsync(
        ImportMdtShareRequest request,
        [AsParameters] ImageImportServices services,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (Share(request.Path, services.Folders) is not { } share)
        {
            return ServerProblems.Problem(ServerMessages.ImportNotAShare.With("path", request.Path ?? string.Empty), StatusCodes.Status400BadRequest);
        }

        // Only what the share's own lists name is read, whatever the request says
        MdtShareView view = MdtShare.Read(share);
        List<ImportItem> items =
        [
            .. view.ImageFiles
                .Where(file => file.Found && (request.ImageFiles ?? []).Contains(file.File, StringComparer.OrdinalIgnoreCase))
                .Select(file => new ImportItem(UploadKind.Image, Path.GetFileName(file.File), MdtShare.Resolve(share, file.File), [])),
            .. view.DriverGroups
                .Where(group => (request.DriverGroups ?? []).Contains(group.Id, StringComparer.OrdinalIgnoreCase))
                .Select(group => new ImportItem(
                    UploadKind.Drivers,
                    PackageName(group),
                    null,
                    MdtShare.DriverFolders(share, group.Id),
                    group.Model is null ? null : new HardwareModel(group.Manufacturer, group.Model),
                    $"From the MDT deployment share {share}, {group.Name}.")),
        ];

        return await StartAsync(items, $"{items.Count} images and driver groups of the MDT deployment share {share}.", services, context, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<Results<Accepted, ProblemHttpResult>> StartAsync(
        List<ImportItem> items,
        string detail,
        ImageImportServices services,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return ServerProblems.Problem(ServerMessages.ImportNothing.With(), StatusCodes.Status400BadRequest);
        }

        Actor actor = SettingsEndpoints.SettingsActor(context);

        if (!services.Imports.TryStart(items, actor))
        {
            return ServerProblems.Problem(ServerMessages.ImportBusy.With(), StatusCodes.Status409Conflict);
        }

        services.Database.AuditEvents.Add(AuditEvents.Create(AuditActions.ImportStarted, null, actor, services.TimeProvider.GetUtcNow(), detail));
        await services.Database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Accepted((string?)null);
    }

    // The share's full path when it is one, in an import folder.
    private static string? Share(string? path, ImportFolders folders) =>
        folders.Allowed(path) is { } allowed && MdtShare.IsShare(allowed) ? allowed : null;

    // "Out-of-Box Drivers\Dell Inc.\Latitude 7440" becomes "Dell Inc. Latitude 7440"
    private static string PackageName(MdtDriverGroup group)
    {
        string[] parts = group.Name.Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length > 1 ? string.Join(' ', parts[1..]) : group.Name;
    }
}
