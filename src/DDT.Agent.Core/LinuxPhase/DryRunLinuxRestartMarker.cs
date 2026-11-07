// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.InstalledPhase;

namespace DDT.Agent.LinuxPhase;

// A dry run restarts by starting its agent over, so a restart it asked for always happened.
public sealed class DryRunLinuxRestartMarker(AgentLog log) : IRestartMarker
{
    public bool IsSet => false;

    public void Set() =>
        log.Information($"Dry run: in the installed Linux the due restart would be recorded in {LinuxAgentPaths.RunFolder}/{LinuxRestartMarker.FileName}, which the restart clears.");
}
