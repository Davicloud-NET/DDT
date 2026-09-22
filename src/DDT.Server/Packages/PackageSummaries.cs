// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Packages;

namespace DDT.Server.Packages;

public static class PackageSummaries
{
    public static PackageSummary From(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);

        return new PackageSummary(
            package.Id,
            package.Name,
            package.Kind,
            package.Sha256,
            package.SizeBytes,
            package.ExpandedBytes,
            package.FileCount,
            PackageTargets.Read(package),
            package.Description,
            package.OriginalFileName,
            package.UploadedUtc,
            package.UploadedByName);
    }
}
