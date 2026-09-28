// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics.CodeAnalysis;
using DDT.Contracts.Images;
using DDT.Contracts.Packages;
using DDT.Server.Data;
using DDT.Server.Packages;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Images;

// Helpers for the library rows that uploads add.
internal static class LibraryEntries
{
    // Answers an upload that already completed. It returns the entries of its file as they are now.
    public static async Task<UploadCompletion> ExistingAsync(
        DdtDbContext database,
        ImageUpload upload,
        string sha256,
        CancellationToken cancellationToken)
    {
        if (upload.Kind == UploadKind.Image)
        {
            return new UploadCompletion(
                UploadCompletionStatus.Existing,
                await ImagesOfAsync(database, sha256, cancellationToken).ConfigureAwait(false));
        }

        PackageKind kind = PackageKindOf(upload.Kind);
        Package? package = await database.Packages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Sha256 == sha256 && p.Kind == kind, cancellationToken)
            .ConfigureAwait(false);

        // The package was deleted after this upload added it.
        return package is null
            ? new UploadCompletion(UploadCompletionStatus.NotFound, [])
            : new UploadCompletion(UploadCompletionStatus.Existing, [], Package: PackageSummaries.From(package));
    }

    public static async Task<IReadOnlyList<ImageSummary>> ImagesOfAsync(DdtDbContext database, string sha256, CancellationToken cancellationToken)
    {
        List<Image> images = await database.Images
            .AsNoTracking()
            .Where(i => i.Sha256 == sha256)
            .OrderBy(i => i.WimIndex)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. images.Select(ImageSummaries.From)];
    }

    public static PackageKind PackageKindOf(UploadKind kind) => kind == UploadKind.Drivers ? PackageKind.Drivers : PackageKind.Files;

    // A WIM's XML is whatever its author wrote, and PostgreSQL refuses a value longer than its column.
    [return: NotNullIfNotNull(nameof(value))]
    public static string? Bounded(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
