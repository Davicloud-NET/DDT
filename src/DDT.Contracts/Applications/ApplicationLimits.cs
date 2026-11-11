// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Applications;

public static class ApplicationLimits
{
    // Applications and bundles alike.
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 500;
    public const int MaxPublisherLength = 100;
    public const int MaxVersionLength = 50;
    public const int MaxCommandLength = 4000;
    public const int MaxExitCodes = 16;
    public const int MaxDependencies = 64;
    public const int MaxBundleMembers = 256;
    public const int MinTimeoutMinutes = 1;
    public const int MaxTimeoutMinutes = 24 * 60;
    public const int DefaultTimeoutMinutes = 60;
    public const int MaxWingetIdLength = 200;
    public const int MaxWingetVersionLength = 100;

    // A registry key name has at most 255 characters.
    public const int MaxUninstallKeyLength = 255;
    public const int MaxDetectionPathLength = 1024;
}
