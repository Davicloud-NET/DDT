// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Packages;

public static class PackageLimits
{
    public const int MaxNameLength = 256;

    public const int MaxDescriptionLength = 1024;

    public const int MaxTargets = 64;

    public const int MaxEntries = 200_000;

    // A driver pack of every model a vendor makes stays well below this; more is a zip bomb.
    public const long MaxExpandedBytes = 64L * 1024 * 1024 * 1024;

    // Under Windows' 260 character path limit once the agent's folder for the package is put in front.
    public const int MaxEntryNameLength = 240;

    public const int MaxDepth = 32;
}
