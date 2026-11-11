// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Applications;
using DDT.Contracts.Applications;

namespace DDT.Agent.Tests;

// Finds what Installed holds, and records what it was asked.
internal sealed class FakeApplicationDetector : IApplicationDetector
{
    public HashSet<ApplicationDetection> Installed { get; } = [];

    public List<ApplicationDetection> Asked { get; } = [];

    public bool IsInstalled(ApplicationDetection detection)
    {
        Asked.Add(detection);

        return Installed.Contains(detection);
    }
}
