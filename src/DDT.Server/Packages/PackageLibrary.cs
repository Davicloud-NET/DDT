// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Messages;
using DDT.Contracts.Packages;
using DDT.Server.BootImage;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Endpoints;
using DDT.Server.Images;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Packages;

// Applies administrators' changes to the packages in the library. The boot image page lists the packages that go into
// the boot image by name, so the page is pushed again when that list changes.
internal sealed class PackageLibrary(
    DdtDbContext database,
    ImageStore store,
    BootImageCatalog bootImage,
    LiveNotifier live,
    TimeProvider timeProvider)
{
    // The last save wins. Packages are edited rarely, and only by administrators. Both values are null when there's no
    // such package.
    public async Task<(PackageSummary? Summary, FieldProblems? Problems)> UpdateAsync(
        Guid id,
        UpdatePackageRequest request,
        Actor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Package? package = await database.Packages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);

        if (package is null)
        {
            return (null, null);
        }

        string name = request.Name?.Trim() ?? "";
        string? description = request.Description?.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();

        if (Problems(package, request, name, description) is { Count: > 0 } problems)
        {
            return (null, problems);
        }

        IReadOnlyList<HardwareModel> targets = PackageTargets.Clean(request.Targets);
        bool wasInBootImage = package.BootImage;
        string previousName = package.Name;

        package.Name = name;
        package.Description = string.IsNullOrEmpty(description) ? null : description;
        package.Targets = PackageTargets.Write(targets);
        package.BootImage = request.BootImage ?? package.BootImage;

        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.PackageChanged,
            package.Id.ToString("D"),
            actor,
            timeProvider.GetUtcNow(),
            Described(package, targets, wasInBootImage)));

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        PackageSummary summary = PackageSummaries.From(package);
        live.PackageChanged(summary);

        if (wasInBootImage != package.BootImage || (package.BootImage && previousName != package.Name))
        {
            live.BootImageChanged(await bootImage.ViewAsync(database, cancellationToken).ConfigureAwait(false));
        }

        return (summary, null);
    }

    // Runs under the library lock. That way an upload of the same file can't add a row for the stored file while it's
    // being deleted.
    public async Task<LibraryDeletion> DeleteAsync(Guid id, Actor actor, CancellationToken cancellationToken)
    {
        bool wasInBootImage;

        await using (await store.LibraryLock.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            Package? package = await database.Packages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);

            if (package is null)
            {
                return LibraryDeletion.NotFound;
            }

            bool inUse = await ActiveArtifacts.Of(database)
                .AnyAsync(a => a.Kind != ArtifactKind.Image && a.SourceId == id, cancellationToken)
                .ConfigureAwait(false);

            if (inUse)
            {
                return LibraryDeletion.InUse;
            }

            wasInBootImage = package.BootImage;
            database.Packages.Remove(package);
            database.AuditEvents.Add(AuditEvents.Create(
                AuditActions.PackageDeleted,
                package.Id.ToString("D"),
                actor,
                timeProvider.GetUtcNow(),
                $"{package.Name}, {package.Kind}, SHA-256 {package.Sha256}."));

            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // The row is gone, so delete the stored file too, unless an image or another package still uses it.
            await store.DeleteObjectIfUnreferencedAsync(database, package.Sha256, CancellationToken.None).ConfigureAwait(false);
        }

        live.PackagesRemoved([id]);

        if (wasInBootImage)
        {
            live.BootImageChanged(await bootImage.ViewAsync(database, cancellationToken).ConfigureAwait(false));
        }

        return LibraryDeletion.Deleted;
    }

    // Only drivers go into the boot image. WinPE loads drivers, and nothing runs a files package there before a
    // sequence does.
    private static FieldProblems Problems(Package package, UpdatePackageRequest request, string name, string? description)
    {
        FieldProblems problems = new();

        if (name.Length is 0 or > PackageLimits.MaxNameLength || name.Any(char.IsControl))
        {
            problems.Add("name", ServerMessages.NameLength.With("max", PackageLimits.MaxNameLength));
        }

        if (description?.Length > PackageLimits.MaxDescriptionLength)
        {
            problems.Add("description", ServerMessages.DescriptionLength.With("max", PackageLimits.MaxDescriptionLength));
        }

        if (PackageTargets.Problem(package.Kind, request.Targets) is { } targetProblem)
        {
            problems.Add("targets", targetProblem);
        }

        if (request.BootImage == true && package.Kind != PackageKind.Drivers)
        {
            problems.Add("bootImage", ServerMessages.PackageBootImageDriversOnly.With());
        }

        return problems;
    }

    private static string Described(Package package, IReadOnlyList<HardwareModel> targets, bool wasInBootImage)
    {
        string bootImageChange = (wasInBootImage, package.BootImage) switch
        {
            (false, true) => " Added to the Windows PE boot image.",
            (true, false) => " Taken out of the Windows PE boot image.",
            _ => "",
        };

        return (targets.Count == 0
            ? $"{package.Name}, no targets."
            : $"{package.Name}, for {string.Join("; ", targets.Select(t => $"{t.Manufacturer ?? "any maker"} {t.Model}"))}.") + bootImageChange;
    }
}
