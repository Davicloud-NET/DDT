// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Globalization;

namespace DDT.Agent.Applications;

// The service runs as SYSTEM, so its own environment expands the variables as SYSTEM sees them.
public sealed class SystemDetectionFiles : IDetectionFiles
{
    public string Expand(string path) => Environment.ExpandEnvironmentVariables(path);

    public bool Exists(string path) => File.Exists(path);

    // The fixed version numbers, not the version text, which may hold anything.
    public string? VersionOf(string path)
    {
        FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);

        return info is { FileMajorPart: 0, FileMinorPart: 0, FileBuildPart: 0, FilePrivatePart: 0 }
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}");
    }
}
