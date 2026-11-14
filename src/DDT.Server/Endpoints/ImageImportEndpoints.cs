// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;
using DDT.Contracts.Import;
using DDT.Contracts.Machines;
using DDT.Contracts.Messages;
using DDT.Server.Applications;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Import;
using DDT.Server.Machines;
using DDT.Server.WindowsPackages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

// Imports from the server's own disks: a WIM, ESD or ISO, an MDT deployment share's images, drivers and applications, and
// the languages of the Languages and Optional Features ISO.
// A browser upload of five gigabytes is the slowest part of a first deployment, and an MDT shop has the files there already.
public static class ImageImportEndpoints
{
    public static RouteGroupBuilder MapImageImportEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", Read).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/", ImportFilesAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/mdt/inspect", InspectShareAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/mdt", ImportShareAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/languages/inspect", InspectLanguagesAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/languages", ImportLanguagesAsync).RequireAuthorization(DdtPolicies.Administrator);

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

        return await StartAsync(new ImportStart(items, null, $"{items.Count} files."), services, context, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Results<Ok<MdtShareView>, ProblemHttpResult>> InspectShareAsync(
        InspectMdtShareRequest request,
        ImportFolders folders,
        ApplicationViews applications,
        CancellationToken cancellationToken)
    {
        if (Share(request.Path, folders) is not { } share)
        {
            return ServerProblems.Problem(ServerMessages.ImportNotAShare.With("path", request.Path ?? string.Empty), StatusCodes.Status400BadRequest);
        }

        ApplicationRows rows = await applications.RowsAsync(cancellationToken).ConfigureAwait(false);
        HashSet<string> names = [.. rows.Applications.Values.Select(a => a.NormalizedName), .. rows.Bundles.Select(b => b.NormalizedName)];

        return TypedResults.Ok(MdtShare.Read(share, names));
    }

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
        MdtShareView view = MdtShare.Read(share, new HashSet<string>());
        IReadOnlyList<MdtShareApplication> entries = MdtApplications.Read(share);
        MdtApplicationChoice choice = new(
            share,
            entries,
            [.. entries.Where(entry => (request.Applications ?? []).Any(id => MdtApplications.Key(id) == entry.Id))]);
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

        string detail = $"{items.Count} images and driver groups and {choice.Chosen.Count} applications and bundles of the MDT deployment share {share}.";

        return await StartAsync(new ImportStart(items, choice, detail), services, context, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Results<Ok<LanguageIsoView>, ProblemHttpResult>> InspectLanguagesAsync(
        InspectLanguageIsoRequest request,
        ImportFolders folders,
        CancellationToken cancellationToken)
    {
        LanguageIsoRead read = await ReadLanguageIsoAsync(request.Path, folders, cancellationToken).ConfigureAwait(false);

        return read.Problem is { } problem
            ? problem
            : TypedResults.Ok(new LanguageIsoView(read.Iso, LanguageIsos.Languages(read.Names), LanguageIsos.Suffix(read.Iso)));
    }

    // One Windows package per language the ISO has. Languages it does not have are left out.
    private static async Task<Results<Accepted, ProblemHttpResult>> ImportLanguagesAsync(
        ImportLanguagesRequest request,
        [AsParameters] ImageImportServices services,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        LanguageIsoRead read = await ReadLanguageIsoAsync(request.Path, services.Folders, cancellationToken).ConfigureAwait(false);
        string suffix = request.Suffix?.Trim() ?? "";

        if (read.Problem is { } problem)
        {
            return problem;
        }

        if (suffix.Length > LanguageIsos.MaxSuffixLength || suffix.Any(char.IsControl))
        {
            return ServerProblems.Problem(ServerMessages.ImportSuffix.With("max", LanguageIsos.MaxSuffixLength), StatusCodes.Status400BadRequest);
        }

        List<ImportItem> items =
        [
            .. LanguageIsos.Languages(read.Names)
                .Where(language => (request.Languages ?? []).Contains(language, StringComparer.OrdinalIgnoreCase))
                .Select(language => new ImportItem(
                    UploadKind.WindowsPackage,
                    suffix.Length == 0 ? $"{language} language pack" : $"{language} language pack {suffix}",
                    read.Iso,
                    [],
                    Description: $"From {Path.GetFileName(read.Iso)}.",
                    IsoFiles: LanguageIsos.FilesOf(read.Names, language))),
        ];
        ImportStart start = new(items, null, $"{items.Count} languages of {read.Iso}.");

        return await StartAsync(start, services, context, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<LanguageIsoRead> ReadLanguageIsoAsync(string? path, ImportFolders folders, CancellationToken cancellationToken)
    {
        if (folders.Allowed(path) is not { } iso || !File.Exists(iso) || !await IsoImages.IsIsoAsync(iso, cancellationToken).ConfigureAwait(false))
        {
            return LanguageIsoRead.Refused(ServerMessages.ImportNotAnIso.With("path", path ?? string.Empty), StatusCodes.Status400BadRequest);
        }

        try
        {
            return await LanguageIsos.ListAsync(iso, cancellationToken).ConfigureAwait(false) is { } names
                ? new LanguageIsoRead(iso, names, null)
                : LanguageIsoRead.Refused(ServerMessages.IsoNoLanguages.With(), StatusCodes.Status422UnprocessableEntity);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            return LanguageIsoRead.Refused(ServerMessages.ImportUnreadable.With("reason", exception.Message), StatusCodes.Status422UnprocessableEntity);
        }
    }

    private static async Task<Results<Accepted, ProblemHttpResult>> StartAsync(
        ImportStart start,
        ImageImportServices services,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (start.Items.Count + (start.Choice?.Chosen.Count ?? 0) == 0)
        {
            return ServerProblems.Problem(ServerMessages.ImportNothing.With(), StatusCodes.Status400BadRequest);
        }

        Actor actor = SettingsEndpoints.SettingsActor(context);

        if (!services.Imports.TryStart(start.Items, actor, start.Choice))
        {
            return ServerProblems.Problem(ServerMessages.ImportBusy.With(), StatusCodes.Status409Conflict);
        }

        services.Database.AuditEvents.Add(AuditEvents.Create(AuditActions.ImportStarted, null, actor, services.TimeProvider.GetUtcNow(), start.Detail));
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

    private sealed record ImportStart(List<ImportItem> Items, MdtApplicationChoice? Choice, string Detail);

    // The ISO's full path and the names of its cabinets, or the problem that says why there are none.
    private sealed record LanguageIsoRead(string Iso, IReadOnlyList<string> Names, ProblemHttpResult? Problem)
    {
        public static LanguageIsoRead Refused(ServerMessage message, int statusCode) => new("", [], ServerProblems.Problem(message, statusCode));
    }
}
