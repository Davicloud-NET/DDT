// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Applications;

// The files a detection rule looks for, as SYSTEM sees them.
public interface IDetectionFiles
{
    // With %Variables% expanded.
    string Expand(string path);

    bool Exists(string path);

    // Its file version as four numbers, such as 23.1.0.0, or null when it has none.
    string? VersionOf(string path);
}
