// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Messages;
using DDT.Contracts.Packages;

namespace DDT.Server.Packages;

// The Windows builds an operator marks a Windows package for: the third number of Windows' version, such as 26100.
public static class WindowsBuilds
{
    // What's wrong with the builds a request sets, or null. Null builds leave the package's as they are.
    public static ServerMessage? Problem(PackageKind kind, IReadOnlyList<int>? builds)
    {
        if (builds is not { Count: > 0 })
        {
            return null;
        }

        if (kind != PackageKind.WindowsPackage)
        {
            return ServerMessages.PackageBuildsWindowsOnly.With();
        }

        if (builds.Any(build => build is < PackageLimits.MinBuild or > PackageLimits.MaxBuild))
        {
            return ServerMessages.PackageBuild.With();
        }

        return builds.Count > PackageLimits.MaxBuilds ? ServerMessages.PackageBuilds.With("max", PackageLimits.MaxBuilds) : null;
    }

    public static IReadOnlyList<int> Clean(IReadOnlyList<int> builds)
    {
        ArgumentNullException.ThrowIfNull(builds);

        return [.. builds.Distinct().Order()];
    }

    // 26200 for 10.0.26200.6584. Null for a version without a build.
    public static int? Of(string? version)
    {
        string[] parts = version?.Split('.') ?? [];

        return parts.Length >= 3 && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int build) ? build : null;
    }
}
