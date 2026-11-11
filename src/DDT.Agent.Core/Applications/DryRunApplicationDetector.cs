// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;

namespace DDT.Agent.Applications;

// Finds nothing, so a dry run installs every application, whatever this computer has.
public sealed class DryRunApplicationDetector(AgentLog log) : IApplicationDetector
{
    public bool IsInstalled(ApplicationDetection detection)
    {
        ArgumentNullException.ThrowIfNull(detection);

        log.Information($"Dry run: {ApplicationDetections.Describe(detection)} is not looked for, and taken as missing.");

        return false;
    }
}
