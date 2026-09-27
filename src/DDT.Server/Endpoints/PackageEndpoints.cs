// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Packages;
using DDT.Server.Authentication;
using DDT.Server.BootImage;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Packages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Endpoints;

// Packages are uploaded through /api/images/uploads with their kind, so a proxy that lets large uploads through
// there needs no second rule. Their contents run as SYSTEM on every machine that gets them, so only an
// administrator changes the library.
public static class PackageEndpoints
{
    public static RouteGroupBuilder MapPackageEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<PackageSummary>>> ListAsync(DdtDbContext database, CancellationToken cancellationToken)
    {
        List<Package> packages = await database.Packages.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<PackageSummary>>(
        [
            .. packages
                .OrderBy(p => p.Kind)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Id)
                .Select(PackageSummaries.From),
        ]);
    }

    // The last save wins: packages are edited rarely, and only by administrators. Only drivers go into the boot image:
    // Windows PE loads drivers, and nothing runs a files package there before a sequence does.
    private static async Task<Results<Ok<PackageSummary>, NotFound, ValidationProblem>> UpdateAsync(
        Guid id,
        UpdatePackageRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        BootImageCatalog bootImage,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        Package? package = await database.Packages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);

        if (package is null)
        {
            return TypedResults.NotFound();
        }

        Dictionary<string, string[]> problems = [];
        string name = request.Name?.Trim() ?? "";
        string? description = request.Description?.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();

        if (name.Length is 0 or > PackageLimits.MaxNameLength || name.Any(char.IsControl))
        {
            problems["name"] = [$"The name must have 1 to {PackageLimits.MaxNameLength} characters and no control characters."];
        }

        if (description?.Length > PackageLimits.MaxDescriptionLength)
        {
            problems["description"] = [$"The description can have at most {PackageLimits.MaxDescriptionLength} characters."];
        }

        if (PackageTargets.Problem(package.Kind, request.Targets) is { } targetProblem)
        {
            problems["targets"] = [targetProblem];
        }

        if (request.BootImage == true && package.Kind != PackageKind.Drivers)
        {
            problems["bootImage"] = ["Only a driver package can go into the Windows PE boot image."];
        }

        if (problems.Count > 0)
        {
            return TypedResults.ValidationProblem(problems);
        }

        IReadOnlyList<HardwareModel> targets = PackageTargets.Clean(request.Targets);
        bool wasInBootImage = package.BootImage;
        string previousName = package.Name;

        package.Name = name;
        package.Description = string.IsNullOrEmpty(description) ? null : description;
        package.Targets = PackageTargets.Write(targets);
        package.BootImage = request.BootImage ?? package.BootImage;

        string bootImageChange = (wasInBootImage, package.BootImage) switch
        {
            (false, true) => " Added to the Windows PE boot image.",
            (true, false) => " Taken out of the Windows PE boot image.",
            _ => "",
        };

        database.AuditEvents.Add(Audit(
            AuditActions.PackageChanged,
            package,
            user,
            context,
            timeProvider.GetUtcNow(),
            (targets.Count == 0
                ? $"{package.Name}, no targets."
                : $"{package.Name}, for {string.Join("; ", targets.Select(t => $"{t.Manufacturer ?? "any maker"} {t.Model}"))}.") + bootImageChange));

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        live.PackagesChanged();

        // The boot image page lists the flagged packages by name.
        if (wasInBootImage != package.BootImage || (package.BootImage && previousName != package.Name))
        {
            live.BootImageChanged(await bootImage.ViewAsync(database, cancellationToken).ConfigureAwait(false));
        }

        return TypedResults.Ok(PackageSummaries.From(package));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(
        Guid id,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        ImageStore store,
        BootImageCatalog bootImage,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        bool wasInBootImage;

        // Under the library lock, so an upload of the same file cannot add a row for the stored file while it goes.
        await store.LibraryLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Package? package = await database.Packages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);

            if (package is null)
            {
                return TypedResults.NotFound();
            }

            bool inUse = await ActiveArtifacts.Of(database)
                .AnyAsync(a => a.Kind != ArtifactKind.Image && a.SourceId == id, cancellationToken)
                .ConfigureAwait(false);

            if (inUse)
            {
                return TypedResults.Problem(
                    title: "Machines are waiting to install this package or are installing it. Cancel those runs or let them finish, then delete it.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            wasInBootImage = package.BootImage;
            database.Packages.Remove(package);
            database.AuditEvents.Add(Audit(
                AuditActions.PackageDeleted,
                package,
                user,
                context,
                timeProvider.GetUtcNow(),
                $"{package.Name}, {package.Kind}, SHA-256 {package.Sha256}."));

            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // The row is gone, so the stored file goes too unless an image or another package still uses it.
            await store.DeleteObjectIfUnreferencedAsync(database, package.Sha256, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            store.LibraryLock.Release();
        }

        live.PackagesChanged();

        if (wasInBootImage)
        {
            live.BootImageChanged(await bootImage.ViewAsync(database, cancellationToken).ConfigureAwait(false));
        }

        return TypedResults.NoContent();
    }

    private static AuditEvent Audit(
        string action,
        Package package,
        ClaimsPrincipal user,
        HttpContext context,
        DateTimeOffset now,
        string detail) => new()
        {
            OccurredUtc = now,
            Action = action,
            ActorUserId = Principals.UserId(user),
            ActorName = user.Identity?.Name,
            SubjectId = package.Id.ToString("D"),
            SourceAddress = context.Connection.RemoteIpAddress?.ToString(),
            Detail = StoredText.Bound(detail, AuditEvent.MaxDetailLength),
        };
}
